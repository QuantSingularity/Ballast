using Ballast.Core;
using Ballast.Core.Domain;
using Ballast.Data;
using Ballast.Infrastructure;
using Ballast.MarketData;
using Ballast.Risk;
using Ballast.TestKit;
using Ballast.Treasury;

namespace Ballast.Backend.Tests;

public class SettingsTests
{
    [Fact]
    public void Production_RequiresStrongSecret()
    {
        Assert.Throws<InvalidOperationException>(() => new BallastSettings { Environment = "production", JwtSecret = "short" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new BallastSettings { Environment = "production", JwtSecret = BallastSettings.PlaceholderSecret }.Validate());
        new BallastSettings { Environment = "production", JwtSecret = new string('x', 40) }.Validate();
    }

    [Fact]
    public void Production_RefusesDemoSeedingWithTheDefaultDemoPassword()
    {
        var forced = new BallastSettings { Environment = "staging", JwtSecret = new string('x', 40), SeedDemoDataOverride = true };
        Assert.Throws<InvalidOperationException>(() => forced.Validate());
        new BallastSettings { Environment = "staging", JwtSecret = new string('x', 40), SeedDemoDataOverride = true, DemoPassword = "Custom-Demo-Pass-1" }.Validate();
        Assert.False(new BallastSettings { Environment = "production" }.SeedDemoData);
        Assert.False(new BallastSettings { Environment = "staging" }.AllowSelfRegistration);
        Assert.True(new BallastSettings { Environment = "development" }.AllowSelfRegistration);
        Assert.True(new BallastSettings { Environment = "production", AllowSelfRegistrationOverride = true }.AllowSelfRegistration);
        Assert.True(new BallastSettings { Environment = "development" }.SeedDemoData);
    }

    [Fact]
    public void Development_StillRequiresAtLeast32CharacterSecret()
    {
        Assert.Throws<InvalidOperationException>(() => new BallastSettings { JwtSecret = "" }.Validate());
        new BallastSettings { JwtSecret = new string('x', 32) }.Validate();
    }

    [Fact]
    public void FromEnvironment_ParsesValuesAndDisablesDemoDataInProduction()
    {
        var env = new Dictionary<string, string>
        {
            ["BALLAST_ENV"] = "production",
            ["BALLAST_BASE_CURRENCY"] = "eur",
            ["BALLAST_DUAL_APPROVAL_THRESHOLD"] = "75000",
            ["BALLAST_CORS_ORIGINS"] = "https://a.example, https://b.example"
        };
        var s = BallastSettings.FromEnvironment(k => env.GetValueOrDefault(k));
        Assert.True(s.IsProductionLike);
        Assert.False(s.SeedDemoData);
        Assert.Equal("EUR", s.BaseCurrency);
        Assert.Equal(75000m, s.DualApprovalThreshold);
        Assert.Equal(2, s.CorsOrigins.Length);
    }

    [Fact]
    public void Currencies_RoundByMinorUnit()
    {
        Assert.Equal(1234m, Currencies.Round(1234.5m, "JPY"));
        Assert.Equal(1236m, Currencies.Round(1235.5m, "JPY"));
        Assert.Equal(10.12m, Currencies.Round(10.1249m, "EUR"));
    }
}

public class InfrastructureTests
{
    private static readonly User Alice = new("U-1", "alice@example.com", "Alice", UserRole.Treasurer, "", DateTimeOffset.UnixEpoch);

    [Fact]
    public void PasswordHasher_VerifiesCorrectAndRejectsWrongOrMalformed()
    {
        var h = new PasswordHasher(1000);
        var stored = h.Hash("Correct-Horse-9");
        Assert.True(h.Verify("Correct-Horse-9", stored));
        Assert.False(h.Verify("correct-horse-9", stored));
        Assert.False(h.Verify("x", "garbage"));
        Assert.True(stored != h.Hash("Correct-Horse-9"), "salts must differ");
    }

    [Fact]
    public void PasswordPolicy_EnforcesLengthAndClasses()
    {
        Assert.Throws<ValidationException>(() => PasswordPolicy.Validate("Short1"));
        Assert.Throws<ValidationException>(() => PasswordPolicy.Validate("alllowercase1234"));
        PasswordPolicy.Validate("Long-Enough-Pass1");
    }

    [Fact]
    public void Jwt_RoundTripsClaims()
    {
        var jwt = new JwtTokenService(TestSettings.Create(), TimeProvider.System);
        var c = jwt.Validate(jwt.Issue(Alice, JwtTokenService.AccessType, TimeSpan.FromMinutes(5)), JwtTokenService.AccessType);
        Assert.NotNull(c);
        Assert.Equal("U-1", c!.Subject);
        Assert.Equal(UserRole.Treasurer, c.Role);
    }

    [Fact]
    public void Jwt_RejectsTamperedExpiredWrongTypeAndNoneAlgorithm()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var jwt = new JwtTokenService(TestSettings.Create(), clock);
        var token = jwt.Issue(Alice, JwtTokenService.AccessType, TimeSpan.FromMinutes(5));
        var parts = token.Split('.');
        var forgedPayload = Convert.ToBase64String("""{"sub":"U-1","email":"a@b.c","name":"x","role":"Admin","typ":"access","exp":9999999999,"jti":"1"}"""u8.ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.True(jwt.Validate($"{parts[0]}.{forgedPayload}.{parts[2]}", JwtTokenService.AccessType) is null, "tampered payload");
        Assert.True(jwt.Validate(token, JwtTokenService.RefreshType) is null, "wrong token type");
        var none = Convert.ToBase64String("""{"alg":"none","typ":"JWT"}"""u8.ToArray()).TrimEnd('=');
        Assert.True(jwt.Validate($"{none}.{parts[1]}.", JwtTokenService.AccessType) is null, "alg none");
        Assert.True(jwt.Validate("not.a.jwt", JwtTokenService.AccessType) is null);
        clock.Now = clock.Now.AddMinutes(6);
        Assert.True(jwt.Validate(token, JwtTokenService.AccessType) is null, "expired");
    }

    [Fact]
    public void Jwt_RejectsTokensSignedWithAnotherSecret()
    {
        var a = new JwtTokenService(TestSettings.Create(), TimeProvider.System);
        var b = new JwtTokenService(new BallastSettings { JwtSecret = new string('z', 40) }, TimeProvider.System);
        Assert.True(b.Validate(a.Issue(Alice, JwtTokenService.AccessType, TimeSpan.FromMinutes(5)), JwtTokenService.AccessType) is null);
    }

    [Fact]
    public void AuditLog_ChainVerifiesAndDetectsTampering()
    {
        var db = new BallastDb(null);
        var log = new AuditLog(db, TimeProvider.System);
        for (int i = 0; i < 5; i++) log.Append("tester", "test.action", $"S-{i}", "detail");
        Assert.True(log.Verify().Valid);
        db.AuditEntries[2] = db.AuditEntries[2] with { Detail = "altered" };
        var v = log.Verify();
        Assert.False(v.Valid);
        Assert.Equal(3L, v.BrokenAtSequence);
    }

    [Fact]
    public void AuditLog_DetectsDeletedEntry()
    {
        var db = new BallastDb(null);
        var log = new AuditLog(db, TimeProvider.System);
        for (int i = 0; i < 4; i++) log.Append("tester", "test.action", $"S-{i}", "detail");
        db.AuditEntries.RemoveAt(1);
        Assert.False(log.Verify().Valid);
    }
}

public class MarketDataTests
{
    private static readonly SyntheticRateProvider Rates = new(TestSettings.AsOf);

    [Fact]
    public void Convert_RoundTripsAndUsesUsdPivot()
    {
        decimal eur = FxMath.Convert(Rates, 1000m, "USD", "EUR");
        Assert.Equal(1000.0, (double)FxMath.Convert(Rates, eur, "EUR", "USD"), 1e-6);
        decimal viaGbp = FxMath.Convert(Rates, FxMath.Convert(Rates, 500m, "EUR", "GBP"), "GBP", "CHF");
        Assert.Equal((double)FxMath.Convert(Rates, 500m, "EUR", "CHF"), (double)viaGbp, 1e-6);
    }

    [Fact]
    public void Forward_FollowsCoveredInterestParity()
    {
        decimal spot = Rates.SpotUsd("EUR");
        decimal fwd = FxMath.ForwardUsd(Rates, "EUR", 90);
        Assert.True(fwd > spot, "USD rates exceed EUR rates so EUR trades at a forward premium");
        Assert.True(FxMath.ForwardUsd(Rates, "JPY", 90) > Rates.SpotUsd("JPY"));
        Assert.Equal((double)spot, (double)FxMath.ForwardUsd(Rates, "EUR", 0), 1e-9);
        Assert.Equal(1.0, (double)FxMath.ForwardUsd(Rates, "USD", 90), 1e-12);
    }

    [Fact]
    public void HedgeMarkToMarket_IsZeroAtInceptionAndSignedCorrectly()
    {
        decimal rate = FxMath.ForwardUsd(Rates, "EUR", 60);
        var sell = new Hedge("H", "EUR", HedgeDirection.SellForeign, 1_000_000m, rate, TestSettings.AsOf, TestSettings.AsOf.AddDays(60), "x", HedgeStatus.Active);
        Assert.Equal(0.0, (double)FxMath.HedgeMarkToMarketUsd(Rates, sell, TestSettings.AsOf), 1e-6);
        var below = sell with { ContractRate = rate - 0.01m };
        Assert.True(FxMath.HedgeMarkToMarketUsd(Rates, below, TestSettings.AsOf) < 0, "selling below market is a loss");
        var buy = below with { Direction = HedgeDirection.BuyForeign };
        Assert.True(FxMath.HedgeMarkToMarketUsd(Rates, buy, TestSettings.AsOf) > 0);
    }

    [Fact]
    public void SpotHistory_EndsAtCurrentSpotWithRequestedLength()
    {
        var h = Rates.SpotHistory("GBP", 120);
        Assert.Equal(120, h.Count);
        Assert.Equal((double)Rates.SpotUsd("GBP"), (double)h[^1].SpotUsd, 1e-9);
        Assert.Equal(TestSettings.AsOf, h[^1].Date);
        Assert.True(h.All(p => p.SpotUsd > 0));
    }

    [Fact]
    public void UnsupportedCurrency_IsRejected()
    {
        Assert.Throws<ValidationException>(() => Rates.SpotUsd("XYZ"));
    }
}

public class TreasuryTests
{
    private static readonly SyntheticRateProvider Rates = new(TestSettings.AsOf);

    [Fact]
    public void Netting_IsZeroSumAndReducesVolume()
    {
        var balances = new List<IntercompanyBalance>
        {
            new("1", "A", "B", "USD", 100m), new("2", "B", "C", "USD", 80m), new("3", "C", "A", "USD", 60m),
            new("4", "A", "C", "EUR", 50m), new("5", "D", "B", "GBP", 30m)
        };
        var r = MultilateralNetting.Run(balances, Rates, "USD");
        Assert.Equal(0.0, (double)r.Positions.Sum(p => p.Net), 1e-9);
        Assert.True(r.NetVolume < r.GrossVolume);
        Assert.True(r.Transfers.Count <= r.Positions.Count - 1);
        foreach (var p in r.Positions)
        {
            decimal received = r.Transfers.Where(t => t.ToEntityId == p.EntityId).Sum(t => t.Amount);
            decimal paid = r.Transfers.Where(t => t.FromEntityId == p.EntityId).Sum(t => t.Amount);
            Assert.Equal((double)p.Net, (double)(received - paid), 1e-9);
        }
    }

    [Fact]
    public void Netting_OfPerfectCycleSettlesToNothing()
    {
        var r = MultilateralNetting.Run([new("1", "A", "B", "USD", 100m), new("2", "B", "C", "USD", 100m), new("3", "C", "A", "USD", 100m)], Rates, "USD");
        Assert.Equal(0.0, (double)r.NetVolume, 1e-9);
        Assert.Equal(1.0, (double)r.ReductionPct, 1e-9);
        Assert.Equal(0, r.Transfers.Count);
    }

    [Fact]
    public void Exposure_CountsOnlyDirectionallyMatchingHedgesInTheirBucket()
    {
        var forecast = new Dictionary<string, double[]> { ["EUR"] = Enumerable.Repeat(1000.0, 91).ToArray(), ["USD"] = new double[91] };
        var start = TestSettings.AsOf;
        var hedges = new List<Hedge>
        {
            new("1", "EUR", HedgeDirection.SellForeign, 15_000m, 1.1m, start, start.AddDays(10), "x", HedgeStatus.Active),
            new("2", "EUR", HedgeDirection.BuyForeign, 99_000m, 1.1m, start, start.AddDays(12), "x", HedgeStatus.Active),
            new("3", "EUR", HedgeDirection.SellForeign, 99_000m, 1.1m, start, start.AddDays(12), "x", HedgeStatus.Cancelled)
        };
        var lines = ExposureAggregator.Compute(forecast, start, hedges, Rates, "USD");
        Assert.Equal(3, lines.Count);
        Assert.True(lines.All(l => l.Currency == "EUR"), "base currency is excluded");
        var near = lines.First(l => l.BucketIndex == 0);
        Assert.Equal(30000.0, (double)near.NetNative, 1e-6);
        Assert.Equal(15000.0, (double)near.HedgedNative, 1e-6);
        Assert.Equal(0.5, (double)near.HedgeRatio, 1e-9);
        Assert.Equal(15000.0, (double)near.OpenNative, 1e-6);
        Assert.Equal(0.0, (double)lines.First(l => l.BucketIndex == 1).HedgedNative, 1e-9);
    }

    [Fact]
    public void HedgeEffectiveness_UsesEightyToOneTwentyFiveBand()
    {
        Assert.Equal(EffectivenessStatus.Effective, HedgeEffectiveness.DollarOffset(-950m, 1000m, 1m).Status);
        Assert.Equal(EffectivenessStatus.Ineffective, HedgeEffectiveness.DollarOffset(-700m, 1000m, 1m).Status);
        Assert.Equal(EffectivenessStatus.Ineffective, HedgeEffectiveness.DollarOffset(-1300m, 1000m, 1m).Status);
        Assert.Equal(EffectivenessStatus.NotAssessable, HedgeEffectiveness.DollarOffset(-0.1m, 0.1m, 1m).Status);
    }

    [Fact]
    public void Covenants_ClassifyPassWarningBreach()
    {
        var f = new DebtFacility("F", "E", "Facility", "USD", 100m, 50m, 0.05m, true, TestSettings.AsOf.AddYears(2),
        [
            new("c1", "Min liquidity", CovenantKind.MinLiquidity, 100m),
            new("c2", "Leverage", CovenantKind.MaxNetDebtToEbitda, 3.5m),
            new("c3", "Interest cover", CovenantKind.MinInterestCover, 4m)
        ]);
        var r = CovenantMonitor.Evaluate([f], new CovenantMetrics(105m, 60m, 20m, 4m));
        Assert.Equal(CovenantStatus.Warning, r[0].Status);
        Assert.Equal(CovenantStatus.Pass, r[1].Status);
        Assert.Equal(CovenantStatus.Pass, r[2].Status);
        var breach = CovenantMonitor.Evaluate([f], new CovenantMetrics(90m, 60m, 20m, 4m));
        Assert.Equal(CovenantStatus.Breach, breach[0].Status);
        var noEbitda = CovenantMonitor.Evaluate([f], new CovenantMetrics(500m, 100m, 0m, 4m));
        Assert.Equal(CovenantStatus.Breach, noEbitda[1].Status);
    }

    [Fact]
    public void CashPosition_SumsToBaseAndSharesTotalOne()
    {
        var entities = new List<LegalEntity> { new("E1", "One", "US", "USD") };
        var accounts = new List<BankAccount>
        {
            new("A1", "E1", "BankA", "x", "USD", 1000m, AccountKind.Operating),
            new("A2", "E1", "BankB", "x", "EUR", 1000m, AccountKind.Operating)
        };
        var p = CashPositionCalculator.Compute(entities, accounts, Rates, "USD");
        Assert.Equal(1000.0 + (double)Rates.SpotUsd("EUR") * 1000, (double)p.TotalBase, 0.01);
        Assert.Equal(1.0, (double)p.ByCurrency.Sum(c => c.Share), 0.001);
        Assert.Equal(2, p.ByBank.Count);
    }
}

public class PaymentWorkflowTests
{
    private static readonly BallastSettings Settings = TestSettings.Create();
    private static readonly SyntheticRateProvider Rates = new(Settings.Today);
    private static readonly BankAccount Account = new("ACC", "E", "Bank", "x", "USD", 1_000_000m, AccountKind.Operating);
    private static readonly Actor Maker = new("U-M", "Maker", UserRole.Treasurer);
    private static readonly Actor CheckerA = new("U-A", "Checker A", UserRole.Approver);
    private static readonly Actor CheckerB = new("U-B", "Checker B", UserRole.Approver);
    private readonly PaymentWorkflow _wf = new(Settings, Rates, TimeProvider.System);

    private Payment Draft(decimal amount, bool flagged = false, bool duplicate = false) =>
        _wf.Create(new NewPayment("ACC", "Supplier", amount, "REF", Settings.Today.AddDays(1)), Account, Maker, "P-1", [], flagged, duplicate);

    [Fact]
    public void SmallPayment_NeedsOneApproval_LargeNeedsTwo()
    {
        Assert.Equal(1, Draft(10_000m).RequiredApprovals);
        Assert.Equal(2, Draft(50_000.01m).RequiredApprovals);
        Assert.Equal(1, Draft(50_000m).RequiredApprovals);
    }

    [Fact]
    public void FlaggedOrDuplicatePayment_EscalatesToTwoApprovals()
    {
        Assert.Equal(2, Draft(1_000m, flagged: true).RequiredApprovals);
        var d = Draft(1_000m, duplicate: true);
        Assert.Equal(2, d.RequiredApprovals);
        Assert.True(d.RiskFlags.Any(f => f.Contains("duplicate", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void FullLifecycle_ThroughDualApprovalToRelease()
    {
        var p = _wf.Submit(Draft(75_000m), Maker);
        p = _wf.Approve(p, CheckerA);
        Assert.Equal(PaymentStatus.PendingApproval, p.Status);
        p = _wf.Approve(p, CheckerB);
        Assert.Equal(PaymentStatus.Approved, p.Status);
        p = _wf.Release(p, Maker, Account);
        Assert.Equal(PaymentStatus.Released, p.Status);
    }

    [Fact]
    public void MakerChecker_CreatorCannotApproveAndApproverCannotApproveTwice()
    {
        var admin = new Actor("U-X", "Admin", UserRole.Admin);
        var p = _wf.Submit(_wf.Create(new NewPayment("ACC", "S", 1000m, "", Settings.Today), Account, admin, "P-2", [], false, false), admin);
        Assert.Throws<ForbiddenException>(() => _wf.Approve(p, admin));
        var big = _wf.Submit(Draft(75_000m), Maker);
        var once = _wf.Approve(big, CheckerA);
        Assert.Throws<ConflictException>(() => _wf.Approve(once, CheckerA));
    }

    [Fact]
    public void Roles_AreEnforced()
    {
        var analyst = new Actor("U-N", "Analyst", UserRole.Analyst);
        Assert.Throws<ForbiddenException>(() =>
            _wf.Create(new NewPayment("ACC", "S", 1000m, "", Settings.Today), Account, analyst, "P-3", [], false, false));
        var pending = _wf.Submit(Draft(1000m), Maker);
        Assert.Throws<ForbiddenException>(() => _wf.Approve(pending, Maker));
        Assert.Throws<ForbiddenException>(() => _wf.Reject(pending, Maker, "no"));
    }

    [Fact]
    public void Release_RequiresApprovalDistinctReleaserAndFunds()
    {
        var pending = _wf.Submit(Draft(1000m), Maker);
        Assert.Throws<ConflictException>(() => _wf.Release(pending, Maker, Account));
        var approved = _wf.Approve(pending, CheckerA);
        var admin = new Actor("U-A", "Admin who approved", UserRole.Admin);
        Assert.Throws<ForbiddenException>(() => _wf.Release(approved, admin, Account));
        Assert.Throws<ConflictException>(() => _wf.Release(approved, Maker, Account with { Balance = 10m }));
    }

    [Fact]
    public void Validation_RejectsBadInput()
    {
        Assert.Throws<ValidationException>(() => Draft(0m));
        Assert.Throws<ValidationException>(() => Draft(-5m));
        Assert.Throws<ValidationException>(() =>
            _wf.Create(new NewPayment("ACC", "S", 10m, "", Settings.Today.AddDays(-1)), Account, Maker, "P", [], false, false));
        Assert.Throws<ValidationException>(() =>
            _wf.Create(new NewPayment("ACC", " ", 10m, "", Settings.Today), Account, Maker, "P", [], false, false));
    }

    [Fact]
    public void Reject_RequiresReasonAndPendingState()
    {
        var pending = _wf.Submit(Draft(1000m), Maker);
        Assert.Throws<ValidationException>(() => _wf.Reject(pending, CheckerA, ""));
        var rejected = _wf.Reject(pending, CheckerA, "Wrong beneficiary");
        Assert.Equal(PaymentStatus.Rejected, rejected.Status);
        Assert.Throws<ConflictException>(() => _wf.Approve(rejected, CheckerB));
    }
}

public class RiskTests
{
    [Fact]
    public void CashAtRisk_UsesLowerTailOfCumulativeErrors()
    {
        var samples = Enumerable.Range(0, 100).Select(i => new double[] { i - 50.0 }).ToArray();
        var r = CashAtRiskCalculator.Compute(samples, 1, 0.95);
        Assert.Equal(45.0, r.CashAtRisk, 1.0);
        Assert.True(r.ExpectedShortfall >= r.CashAtRisk);
        Assert.Equal(0.0, CashAtRiskCalculator.Compute([], 30, 0.95).CashAtRisk, 1e-12);
    }

    [Fact]
    public void Stress_CombinedIsWorstAndCoverageFalls()
    {
        var snap = new LiquiditySnapshot(100m, 40m, 25m, 20m, 30m, 20m, 50m);
        var results = StressTester.Run(snap);
        var combined = results.First(r => r.Id == "combined-severe");
        Assert.True(results.All(r => r.LiquidityImpactBase <= 0));
        Assert.True(results.Where(r => r.Id != "combined-severe").All(r => combined.LiquidityImpactBase <= r.LiquidityImpactBase));
        Assert.Equal(6.0, (double)(120m / 20m), 1e-9);
        Assert.True(combined.CoverageRatioAfter < 6m);
    }

    [Fact]
    public void Limits_FlagConcentrationAndLiquidityBreaches()
    {
        var settings = TestSettings.Create();
        var banks = new List<BankPosition> { new("Big", 600m, 0.6m), new("Small", 400m, 0.4m) };
        var limits = LimitMonitor.Evaluate(settings, banks, 1m, [], []);
        Assert.Equal(LimitStatus.Breach, limits.First(l => l.Name.Contains("Big")).Status);
        Assert.Equal(LimitStatus.Breach, limits.First(l => l.Id == "liquidity-buffer").Status);
        var ok = LimitMonitor.Evaluate(settings, [new BankPosition("Only", 1m, 0.2m)], settings.MinLiquidityBuffer * 2, [], []);
        Assert.True(ok.All(l => l.Status == LimitStatus.Ok));
    }
}

public class HedgeAdvisorTests
{
    private static ExposureLine Line(string ccy, int bucket, decimal net, decimal hedged) =>
        new(ccy, ExposureAggregator.Buckets[bucket].Label, bucket, net, net, hedged, net == 0 ? 0 : hedged / Math.Abs(net),
            net - Math.Sign(net) * hedged, net - Math.Sign(net) * hedged);

    [Fact]
    public void Recommendations_ClampTheAgentIntoThePolicyBandAndNetOffExistingHedges()
    {
        var advisor = new HedgeAdvisor(q => q.Select(_ => 1.0).ToList());
        var recs = advisor.Recommend([Line("EUR", 0, 100_000m, 0m), Line("CHF", 2, 80_000m, 0m)], new Dictionary<string, double>());
        Assert.Equal(0.9m, recs[0].TargetRatio);
        Assert.Equal(90_000m, recs[0].RecommendedNotional);
        Assert.Equal(Ballast.Core.Domain.HedgeDirection.SellForeign, recs[0].Direction!.Value);
        Assert.Equal(0.5m, recs[1].TargetRatio);
        Assert.Equal(40_000m, recs[1].RecommendedNotional);
    }

    [Fact]
    public void NoActionWhenAlreadyHedgedAboveTargetAndShortExposureBuysForeign()
    {
        var advisor = new HedgeAdvisor(q => q.Select(_ => 0.0).ToList());
        var recs = advisor.Recommend([Line("JPY", 2, -50_000m, 10_000m), Line("GBP", 0, -100_000m, 0m)], new Dictionary<string, double>());
        Assert.True(recs[0].Direction is null);
        Assert.Equal(0m, recs[0].RecommendedNotional);
        Assert.Equal(Ballast.Core.Domain.HedgeDirection.BuyForeign, recs[1].Direction!.Value);
        Assert.Equal(50_000m, recs[1].RecommendedNotional);
    }

    [Fact]
    public void QueriesCarryBucketCurrentRatioAndRegimeAndZeroExposuresAreSkipped()
    {
        IReadOnlyList<AgentQuery>? seen = null;
        var advisor = new HedgeAdvisor(q => { seen = q; return q.Select(_ => 0.5).ToList(); });
        var recs = advisor.Recommend([Line("EUR", 1, 0m, 0m), Line("GBP", 1, 60_000m, 15_000m)],
            new Dictionary<string, double> { ["GBP"] = 0.8, ["EUR"] = 0.1 });
        Assert.Equal(1, recs.Count);
        Assert.Equal(1, seen!.Count);
        Assert.Equal(1, seen[0].BucketIndex);
        Assert.Equal(0.25, seen[0].CurrentRatio, 1e-9);
        Assert.Equal(0.8, seen[0].HighVolProbability, 1e-9);
        Assert.Contains("high-volatility", recs[0].Rationale);
    }

    [Fact]
    public void MismatchedAgentResponseIsRejected()
    {
        var advisor = new HedgeAdvisor(_ => [0.5]);
        Assert.Throws<InvalidOperationException>(() =>
            advisor.Recommend([Line("EUR", 0, 1m, 0m), Line("GBP", 0, 1m, 0m)], new Dictionary<string, double>()));
    }
}

public class ModelHubTests
{
    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(request.RequestUri!.AbsolutePath);
            return respond(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    private static HttpResponseMessage Json(string body, int status = 200) =>
        new((System.Net.HttpStatusCode)status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static Ballast.Api.Services.ModelHub Hub(ScriptedHandler handler)
    {
        var settings = TestSettings.Create();
        var db = new BallastDb(null);
        DemoSeeder.SeedStatic(db, settings);
        var client = new Ballast.AiClient.AiServiceClient(new HttpClient(handler) { BaseAddress = new Uri("http://model.test") });
        return new Ballast.Api.Services.ModelHub(db, new SyntheticRateProvider(settings.Today), settings, client);
    }

    private const string Assessment =
        """{"results":[{"score":0.5,"threshold":0.7,"robustZ":0,"newCounterparty":false,"flagged":false,"reasons":[]}]}""";

    [Fact]
    public void LostDetector_IsRefittedAndTheCallRetriedOnce()
    {
        int assessCalls = 0;
        var handler = new ScriptedHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/v1/anomaly/fit" => Json("""{"threshold":0.71,"samples":9000}"""),
            "/v1/anomaly/assess-batch" when ++assessCalls == 1 => Json("""{"code":"model_not_fitted","message":"not fitted"}""", 409),
            "/v1/anomaly/assess-batch" => Json(Assessment),
            _ => Json("{}", 404)
        });
        var hub = Hub(handler);
        var result = hub.Assess("ACC-US-001", "Globex Materials", 1000m, "USD", new DateOnly(2026, 11, 9));
        Assert.Equal(0.5, result.Score, 1e-9);
        Assert.Equal(2, handler.Calls.Count(c => c == "/v1/anomaly/fit"));
        Assert.Equal(2, handler.Calls.Count(c => c == "/v1/anomaly/assess-batch"));
    }

    [Fact]
    public void DetectorIsFittedOnceAndRegimeModelsAreCached()
    {
        var handler = new ScriptedHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/v1/anomaly/fit" => Json("""{"threshold":0.71,"samples":9000}"""),
            "/v1/anomaly/assess-batch" => Json(Assessment),
            "/v1/regimes/fit" => Json("""{"means":[0,0],"sigmas":[0.005,0.02],"transition":[[0.97,0.03],[0.08,0.92]],"initial":[0.7,0.3]}"""),
            "/v1/regimes/filter" => Json("""{"probabilities":[0.3,0.7]}"""),
            _ => Json("{}", 404)
        });
        var hub = Hub(handler);
        hub.Assess("ACC-US-001", "A", 1m, "USD", new DateOnly(2026, 11, 9));
        hub.Assess("ACC-US-001", "B", 2m, "USD", new DateOnly(2026, 11, 9));
        Assert.Equal(1, handler.Calls.Count(c => c == "/v1/anomaly/fit"));
        Assert.Equal(0.7, hub.HighVolProbability("EUR"), 1e-9);
        Assert.Equal(0.7, hub.HighVolProbability("EUR"), 1e-9);
        Assert.Equal(0.0, hub.HighVolProbability("USD"), 1e-9);
        Assert.Equal(1, handler.Calls.Count(c => c == "/v1/regimes/fit"));
        Assert.Equal(1, handler.Calls.Count(c => c == "/v1/regimes/filter"));
    }

    [Fact]
    public void UnreachableModelService_BecomesAServiceUnavailableError()
    {
        var handler = new ScriptedHandler(_ => throw new HttpRequestException("connection refused"));
        var hub = Hub(handler);
        var ex = Assert.Throws<ServiceUnavailableException>(() => hub.Hmm("EUR"));
        Assert.Contains("unreachable", ex.Message);
        Assert.Equal(503, ex.StatusCode);
    }

    [Fact]
    public void ServiceErrorsAreSurfacedAsBadGateway()
    {
        var hub = Hub(new ScriptedHandler(_ => Json("""{"code":"validation_error","message":"series: too short"}""", 400)));
        var ex = Assert.Throws<BallastException>(() => hub.Hmm("EUR"));
        Assert.Equal(502, ex.StatusCode);
        Assert.Contains("series: too short", ex.Message);
    }
}
