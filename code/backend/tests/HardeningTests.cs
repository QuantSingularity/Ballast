using System.Net;
using System.Net.Http.Json;
using Ballast.Api.Services;
using Ballast.Core;
using Ballast.Core.Domain;
using Ballast.MarketData;
using Ballast.Risk;
using Ballast.TestKit;
using Ballast.Treasury;

namespace Ballast.Backend.Tests;

public class ProxyTrustTests
{
    [Fact]
    public void TrustedProxies_AreParsedFromEnvironmentAndValidated()
    {
        var s = BallastSettings.FromEnvironment(k => k switch
        {
            "BALLAST_SECRET_KEY" => new string('k', 40),
            "BALLAST_TRUST_PROXY" => "true",
            "BALLAST_TRUSTED_PROXIES" => "10.1.0.0/16, 192.0.2.7/32",
            _ => null
        });
        Assert.Equal(2, s.TrustedProxyNetworks.Length);
        s.Validate();
        Assert.Equal(BallastSettings.DefaultTrustedProxyNetworks.Length, new BallastSettings().TrustedProxyNetworks.Length);
        Assert.Throws<InvalidOperationException>(() =>
            new BallastSettings { JwtSecret = new string('k', 40), TrustedProxyNetworks = ["not-a-network"] }.Validate());
        Assert.Throws<InvalidOperationException>(() =>
            new BallastSettings { JwtSecret = new string('k', 40), TrustProxy = true, TrustedProxyNetworks = [] }.Validate());
    }

    private static async Task<List<HttpStatusCode>> LoginBurstAsync(ApiServer s, params string[] forwardedFor)
    {
        var codes = new List<HttpStatusCode>();
        foreach (var ip in forwardedFor)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { email = "x@example.com", password = "y" })
            };
            req.Headers.Add("X-Forwarded-For", ip);
            using var res = await s.Http.SendAsync(req);
            codes.Add(res.StatusCode);
        }
        return codes;
    }

    [Fact]
    public async Task ForwardedHeaders_FromAnUntrustedPeerAreIgnoredEvenWhenTrustProxyIsOn()
    {
        // The test client connects from loopback, which is outside the configured proxy network, so its
        // X-Forwarded-For values must not create separate rate-limit buckets.
        var settings = new BallastSettings
        {
            Environment = "development",
            JwtSecret = TestSettings.Secret,
            PasswordIterations = 1000,
            AsOfDate = TestSettings.AsOf,
            AuthRateLimitPerMinute = 3,
            TrustProxy = true,
            TrustedProxyNetworks = ["203.0.113.0/24"],
            AiServiceUrl = AiServiceProcess.Url
        };
        await using var s = await ApiServer.StartAsync(settings);
        var codes = await LoginBurstAsync(s, "10.5.0.1", "10.5.0.2", "10.5.0.3", "10.5.0.4", "10.5.0.5");
        Assert.True(codes.Contains(HttpStatusCode.TooManyRequests), "spoofed headers from an untrusted peer must be ignored: " + string.Join(",", codes));
    }
}

public class LoginThrottleTests
{
    private const string Victim = "victim@example.com";

    [Fact]
    public void FailuresLockTheCallerNotTheAccountOwner()
    {
        var t = new LoginThrottle(new FakeClock(DateTimeOffset.UtcNow));
        for (int i = 0; i < LoginThrottle.MaxFailuresPerClient; i++) t.RecordFailure(Victim, "198.51.100.1");
        Assert.True(t.IsLocked(Victim, "198.51.100.1"));
        Assert.False(t.IsLocked(Victim, "198.51.100.2"), "a different client must not be locked out by someone else's guesses");
    }

    [Fact]
    public void DistributedGuessingEventuallyLocksTheAccountForEveryone()
    {
        var t = new LoginThrottle(new FakeClock(DateTimeOffset.UtcNow));
        for (int i = 0; i < LoginThrottle.MaxFailuresPerAccount; i++) t.RecordFailure(Victim, $"203.0.113.{i}");
        Assert.True(t.IsLocked(Victim, "192.0.2.250"));
    }

    [Fact]
    public void LockExpires_FailuresDecay_AndSuccessClears()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var t = new LoginThrottle(clock);
        for (int i = 0; i < LoginThrottle.MaxFailuresPerClient; i++) t.RecordFailure(Victim, "a");
        Assert.True(t.IsLocked(Victim, "a"));
        clock.Now = clock.Now.AddMinutes(6);
        Assert.False(t.IsLocked(Victim, "a"));

        for (int i = 0; i < LoginThrottle.MaxFailuresPerClient - 1; i++) t.RecordFailure("other@example.com", "a");
        clock.Now = clock.Now.AddMinutes(16);
        t.RecordFailure("other@example.com", "a");
        Assert.False(t.IsLocked("other@example.com", "a"), "old failures should have decayed");

        for (int i = 0; i < LoginThrottle.MaxFailuresPerClient - 1; i++) t.RecordFailure("third@example.com", "a");
        t.RecordSuccess("third@example.com", "a");
        t.RecordFailure("third@example.com", "a");
        Assert.False(t.IsLocked("third@example.com", "a"));
    }

    [Fact]
    public void StoreIsBounded_AndKeepsActiveLocksWhenFlooded()
    {
        var t = new LoginThrottle(new FakeClock(DateTimeOffset.UtcNow), maxEntries: 50);
        for (int i = 0; i < LoginThrottle.MaxFailuresPerClient; i++) t.RecordFailure(Victim, "attacker");
        Assert.True(t.IsLocked(Victim, "attacker"));
        for (int i = 0; i < 500; i++) t.RecordFailure($"user{i}@example.com", "flood");
        Assert.True(t.Count <= 50, $"store grew to {t.Count}");
        Assert.True(t.IsLocked(Victim, "attacker"), "an active lock should outlive a flood of throwaway emails");
    }

    [Fact]
    public void OversizedEmailsAreTruncatedSoKeysStayBounded()
    {
        var t = new LoginThrottle(new FakeClock(DateTimeOffset.UtcNow));
        t.RecordFailure(new string('a', 1_000_000), "x");
        Assert.Equal(2, t.Count);
    }

    [Fact]
    public async Task LockoutIsPerClientThroughTheApi()
    {
        var settings = new BallastSettings
        {
            Environment = "development",
            JwtSecret = TestSettings.Secret,
            PasswordIterations = 1000,
            AsOfDate = TestSettings.AsOf,
            AuthRateLimitPerMinute = 1000,
            DemoPassword = TestSettings.DemoPassword,
            TrustProxy = true,
            AiServiceUrl = AiServiceProcess.Url
        };
        await using var s = await ApiServer.StartAsync(settings);

        async Task<HttpStatusCode> Login(string ip, string password)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { email = "analyst@ballast.example", password })
            };
            req.Headers.Add("X-Forwarded-For", ip);
            using var res = await s.Http.SendAsync(req);
            return res.StatusCode;
        }

        for (int i = 0; i < LoginThrottle.MaxFailuresPerClient; i++) await Login("10.9.9.9", "wrong");
        Assert.Equal((HttpStatusCode)423, await Login("10.9.9.9", TestSettings.DemoPassword));
        Assert.Equal(HttpStatusCode.OK, await Login("10.9.9.8", TestSettings.DemoPassword));
    }
}

public class ReleaseNettingTests
{
    private static readonly BallastSettings Settings = TestSettings.Create();
    private static readonly SyntheticRateProvider Rates = new(Settings.Today);
    private static readonly BankAccount Account = new("ACC", "E", "Bank", "x", "USD", 1_000_000m, AccountKind.Operating);
    private static readonly Actor Maker = new("U-M", "Maker", UserRole.Treasurer);
    private static readonly Actor CheckerA = new("U-A", "Checker A", UserRole.Approver);
    private static readonly Actor CheckerB = new("U-B", "Checker B", UserRole.Approver);
    private readonly PaymentWorkflow _wf = new(Settings, Rates, TimeProvider.System);

    private Payment Approved(decimal amount)
    {
        var p = _wf.Create(new NewPayment("ACC", "Supplier", amount, "REF", Settings.Today.AddDays(1)), Account, Maker, "P-1", [], false, false);
        p = _wf.Submit(p, Maker);
        p = _wf.Approve(p, CheckerA);
        return p.Status == PaymentStatus.Approved ? p : _wf.Approve(p, CheckerB);
    }

    [Fact]
    public void ReleaseIsCheckedAgainstBalanceNetOfEarlierReleases()
    {
        var first = Approved(600_000m);
        var released = _wf.Release(first, Maker, Account);
        Assert.Equal(PaymentStatus.Released, released.Status);
        Assert.NotNull(released.ReleasedAt);

        var second = Approved(600_000m);
        // Each payment fits the 1,000,000 balance on its own; together they do not.
        Assert.Throws<ConflictException>(() => _wf.Release(second, Maker, Account, alreadyReleased: 600_000m));
        var ok = _wf.Release(second, Maker, Account, alreadyReleased: 400_000m);
        Assert.Equal(PaymentStatus.Released, ok.Status);
    }

    [Fact]
    public void InsufficientBalanceMessageStatesWhatIsAvailable()
    {
        var p = Approved(600_000m);
        var ex = Assert.Throws<ConflictException>(() => _wf.Release(p, Maker, Account, alreadyReleased: 600_000m));
        Assert.Contains("400000", ex.Message.Replace(",", ""));
    }
}

public class PaymentServiceIntegrityTests
{
    private static object Payment(string account, string beneficiary, decimal amount) =>
        new { accountId = account, beneficiary, amount, reference = "TEST", valueDate = "2026-11-09" };

    [Fact]
    public async Task ApprovedPaymentsCannotTogetherOverdrawTheAccount()
    {
        await using var s = await ApiServer.StartAsync();
        var (_, accounts) = await s.GetAsync("/api/v1/cash/accounts");
        decimal balance = accounts!.AsArray().First(a => a!["id"]!.GetValue<string>() == "ACC-US-001")!["balance"]!.GetValue<decimal>();
        decimal each = Math.Floor(balance * 0.6m);

        async Task<string> ApprovedPayment(string beneficiary)
        {
            var (_, p) = await s.PostAsync("/api/v1/payments", "treasurer@ballast.example", Payment("ACC-US-001", beneficiary, each));
            string id = p!["id"]!.GetValue<string>();
            await s.PostAsync($"/api/v1/payments/{id}/submit", "treasurer@ballast.example");
            await s.PostAsync($"/api/v1/payments/{id}/approve", "approver@ballast.example");
            var (_, done) = await s.PostAsync($"/api/v1/payments/{id}/approve", "approver2@ballast.example");
            Assert.Equal("Approved", done!["status"]!.GetValue<string>());
            return id;
        }

        string a = await ApprovedPayment("Overdraft Test One");
        string b = await ApprovedPayment("Overdraft Test Two");
        var (first, released) = await s.PostAsync($"/api/v1/payments/{a}/release", "treasurer@ballast.example");
        Assert.Equal(200, first);
        Assert.NotNull(released!["releasedAt"]);
        var (second, _) = await s.PostAsync($"/api/v1/payments/{b}/release", "treasurer@ballast.example");
        Assert.Equal(409, second);
    }

    [Fact]
    public async Task ConcurrentIdenticalRequests_ExactlyOneIsTheOriginal_AndAuditOrderMatchesIds()
    {
        await using var s = await ApiServer.StartAsync();
        var svc = s.Service<PaymentService>();
        var actor = new Actor("U-002", "Taylor Treasurer", UserRole.Treasurer);
        // A savings account skips the network call to the model service, so the threads really do race on the
        // duplicate check, the id allocation and the insert.
        const int rounds = 40, threads = 8;
        var created = new List<Payment>();
        for (int round = 0; round < rounds; round++)
        {
            var req = new Ballast.Api.Schemas.CreatePaymentRequest("ACC-US-003", $"Race Supplies {round}", 1_000m + round, "RACE", new DateOnly(2026, 11, 9));
            using var barrier = new Barrier(threads);
            var batch = await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Factory.StartNew(
                () => { barrier.SignalAndWait(); return svc.Create(req, actor); }, TaskCreationOptions.LongRunning)));
            int originals = batch.Count(p => !p.RiskFlags.Any(f => f.Contains("duplicate", StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(1, originals, $"round {round}: exactly one request may be the original");
            created.AddRange(batch);
        }
        var ids = created.Select(p => p.Id).ToList();
        Assert.Equal(rounds * threads, ids.Distinct().Count());

        var (_, audit) = await s.GetAsync("/api/v1/audit?limit=500");
        var order = audit!.AsArray()
            .Where(a => a!["action"]!.GetValue<string>() == "payment.create" && ids.Contains(a["subject"]!.GetValue<string>()))
            .OrderBy(a => a!["sequence"]!.GetValue<long>())
            .Select(a => a!["subject"]!.GetValue<string>())
            .ToList();
        Assert.Equal(ids.Count, order.Count);
        Assert.True(order.SequenceEqual(order.OrderBy(x => x, StringComparer.Ordinal)), "audit order must match id allocation order");
        Assert.True((await s.GetAsync("/api/v1/audit/verify")).Body!["valid"]!.GetValue<bool>());
    }
}

public class CashAtRiskReliabilityTests
{
    private static double[][] Window(IEnumerable<double> values) => values.Select(v => new[] { v }).ToArray();

    [Fact]
    public void ThinTail_IsFlooredByANormalFit()
    {
        // 20 errors from -9.5 to 9.5: the empirical 99% tail is just the sample minimum (9.5).
        var errors = Window(Enumerable.Range(0, 20).Select(i => i - 9.5));
        var r99 = CashAtRiskCalculator.Compute(errors, 1, 0.99);
        Assert.Equal("empirical-floored-by-normal", r99.Method);
        // mean 0, sample sd 5.916, z(0.01) = -2.3263, so the normal estimate is about 13.76
        Assert.Equal(13.76, r99.CashAtRisk, 0.05);
        Assert.True(r99.ExpectedShortfall >= r99.CashAtRisk);
        var r95 = CashAtRiskCalculator.Compute(errors, 1, 0.95);
        Assert.True(r99.CashAtRisk >= r95.CashAtRisk, "99% must not be below 95%");
    }

    [Fact]
    public void LongHorizonsOnOverlappingOriginsAreFlaggedLowReliability()
    {
        var samples = Enumerable.Range(0, 40).Select(i => new double[91].Select((_, h) => (h + 1) * (i - 20.0)).ToArray()).ToArray();
        var r = CashAtRiskCalculator.Compute(samples, 91, 0.95);
        Assert.Equal(40, r.Samples);
        Assert.InRange(r.EffectiveSamples, 3.0, 3.2);
        Assert.Equal("low", r.Reliability);
    }

    [Fact]
    public void PlentifulIndependentSamples_UseTheEmpiricalTail()
    {
        var errors = Window(Enumerable.Range(0, 400).Select(i => i - 200.0));
        var r = CashAtRiskCalculator.Compute(errors, 7, 0.95, originSpacingDays: 7);
        Assert.Equal("empirical", r.Method);
        Assert.Equal("adequate", r.Reliability);
        Assert.Equal(180.05, r.CashAtRisk, 0.5);
    }

    [Fact]
    public async Task RiskMetricsEndpointReportsSampleQuality()
    {
        var s = await Shared.Server;
        var (status, m) = await s.GetAsync("/api/v1/risk/metrics");
        Assert.Equal(200, status);
        foreach (var row in m!["cashAtRisk"]!.AsArray())
        {
            Assert.True(row!["samples"]!.GetValue<int>() >= 30, "risk metrics should use the larger calibration set");
            Assert.NotNull(row["reliability"]);
            Assert.NotNull(row["method"]);
            Assert.True(row["effectiveSamples"]!.GetValue<double>() >= 1.0);
        }
    }
}
