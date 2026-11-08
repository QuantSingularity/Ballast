using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Ballast.Infrastructure;
using Ballast.TestKit;

namespace Ballast.Backend.Tests;

public class AuthApiTests
{
    [Fact]
    public async Task Health_IsPublic()
    {
        var s = await Shared.Server;
        var (status, body) = await s.GetAsync("/health", null);
        Assert.Equal(200, status);
        Assert.Equal("ok", body!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task ProtectedRoutes_RequireAuthentication()
    {
        var s = await Shared.Server;
        foreach (var path in new[] { "/api/v1/cash/summary", "/api/v1/forecast", "/api/v1/payments", "/api/v1/audit", "/api/auth/profile" })
        {
            var (status, body) = await s.GetAsync(path, null);
            Assert.Equal(401, status, path);
            Assert.Equal("unauthorized", body!["code"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task InvalidBearerToken_IsRejected()
    {
        var s = await Shared.Server;
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cash/summary");
        req.Headers.Authorization = new("Bearer", "abc.def.ghi");
        using var res = await s.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsTokensAndProfileMatches()
    {
        var s = await Shared.Server;
        var res = await s.Http.PostAsJsonAsync("/api/auth/login", new { email = "approver@ballast.example", password = TestSettings.DemoPassword });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
        Assert.Equal("Approver", json["user"]!["role"]!.GetValue<string>());
        Assert.True(json["accessToken"]!.GetValue<string>().Split('.').Length == 3);
        Assert.True(json["expiresIn"]!.GetValue<int>() > 0);
        Assert.False(json.ToJsonString().Contains("passwordHash", StringComparison.OrdinalIgnoreCase), "hash must never be exposed");
        var (_, profile) = await s.GetAsync("/api/auth/profile", "approver@ballast.example");
        Assert.Equal("approver@ballast.example", profile!["email"]!.GetValue<string>());
    }

    [Fact]
    public async Task Login_WithWrongPasswordOrUnknownUser_GivesSameGenericError()
    {
        var s = await Shared.Server;
        var wrong = await s.Http.PostAsJsonAsync("/api/auth/login", new { email = "analyst@ballast.example", password = "wrong" });
        var unknown = await s.Http.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.com", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_LocksAfterRepeatedFailures()
    {
        await using var s = await ApiServer.StartAsync();
        HttpStatusCode last = HttpStatusCode.OK;
        for (int i = 0; i < 5; i++)
            last = (await s.Http.PostAsJsonAsync("/api/auth/login", new { email = "analyst@ballast.example", password = "bad" })).StatusCode;
        Assert.Equal(HttpStatusCode.Unauthorized, last);
        var locked = await s.Http.PostAsJsonAsync("/api/auth/login", new { email = "analyst@ballast.example", password = TestSettings.DemoPassword });
        Assert.Equal((HttpStatusCode)423, locked.StatusCode);
    }

    [Fact]
    public async Task Register_EnforcesPolicyUniquenessAndDefaultsToAnalyst()
    {
        var s = await Shared.Server;
        var weak = await s.Http.PostAsJsonAsync("/api/auth/register", new { email = "weak@example.com", password = "short", name = "Weak User" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var badEmail = await s.Http.PostAsJsonAsync("/api/auth/register", new { email = "not-an-email", password = "Strong-Passw0rd!", name = "Bad Email" });
        Assert.Equal(HttpStatusCode.BadRequest, badEmail.StatusCode);
        var ok = await s.Http.PostAsJsonAsync("/api/auth/register", new { email = "new.user@example.com", password = "Strong-Passw0rd!", name = "New User" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal("Analyst", JsonNode.Parse(await ok.Content.ReadAsStringAsync())!["user"]!["role"]!.GetValue<string>());
        var dup = await s.Http.PostAsJsonAsync("/api/auth/register", new { email = "NEW.user@example.com", password = "Strong-Passw0rd!", name = "Dup" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task CreateUser_IsAdminOnlyAndValidated()
    {
        var s = await Shared.Server;
        var body = new { email = "made.by.admin@example.com", name = "Made By Admin", password = "Strong-Passw0rd!", role = "Approver" };
        Assert.Equal(403, (await s.PostAsync("/api/auth/users", "treasurer@ballast.example", body)).Status);
        var (created, user) = await s.PostAsync("/api/auth/users", "admin@ballast.example", body);
        Assert.Equal(201, created);
        Assert.Equal("Approver", user!["role"]!.GetValue<string>());
        Assert.Equal(409, (await s.PostAsync("/api/auth/users", "admin@ballast.example", body)).Status);
        Assert.Equal(400, (await s.PostAsync("/api/auth/users", "admin@ballast.example", new { email = "x@example.com", name = "Weak", password = "weak", role = "Analyst" })).Status);
        Assert.Equal(400, (await s.PostAsync("/api/auth/users", "admin@ballast.example", new { email = "y@example.com", name = "Bad Role", password = "Strong-Passw0rd!", role = 99 })).Status);
    }

    [Fact]
    public async Task Refresh_IssuesNewTokenAndRejectsAccessTokenAsRefresh()
    {
        var s = await Shared.Server;
        var login = await s.Http.PostAsJsonAsync("/api/auth/login", new { email = "analyst@ballast.example", password = TestSettings.DemoPassword });
        var json = JsonNode.Parse(await login.Content.ReadAsStringAsync())!;
        var ok = await s.Http.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = json["refreshToken"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var bad = await s.Http.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = json["accessToken"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
    }

    [Fact]
    public async Task RoleChange_IsAdminOnlyAndNotSelfService()
    {
        var s = await Shared.Server;
        var (forbidden, _) = await s.SendAsync(HttpMethod.Patch, "/api/auth/users/U-005/role", "treasurer@ballast.example", new { role = "Admin" });
        Assert.Equal(403, forbidden);
        var (self, _) = await s.SendAsync(HttpMethod.Patch, "/api/auth/users/U-001/role", "admin@ballast.example", new { role = "Analyst" });
        Assert.Equal(403, self);
        var (ok, body) = await s.SendAsync(HttpMethod.Patch, "/api/auth/users/U-005/role", "admin@ballast.example", new { role = "Treasurer" });
        Assert.Equal(200, ok);
        Assert.Equal("Treasurer", body!["role"]!.GetValue<string>());
        await s.SendAsync(HttpMethod.Patch, "/api/auth/users/U-005/role", "admin@ballast.example", new { role = "Analyst" });
    }

    [Fact]
    public async Task AuthEndpoints_AreRateLimited()
    {
        await using var s = await ApiServer.StartAsync(TestSettings.Create(authRateLimit: 3));
        var codes = new List<HttpStatusCode>();
        for (int i = 0; i < 5; i++)
            codes.Add((await s.Http.PostAsJsonAsync("/api/auth/login", new { email = "x@example.com", password = "y" })).StatusCode);
        Assert.True(codes.Contains(HttpStatusCode.TooManyRequests), string.Join(",", codes));
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
    public async Task RateLimit_IgnoresForwardedForUnlessProxyIsTrusted()
    {
        await using var s = await ApiServer.StartAsync(TestSettings.Create(authRateLimit: 3));
        var codes = await LoginBurstAsync(s, "10.0.0.1", "10.0.0.2", "10.0.0.3", "10.0.0.4", "10.0.0.5");
        Assert.True(codes.Contains(HttpStatusCode.TooManyRequests), "spoofed headers must not create separate buckets: " + string.Join(",", codes));
    }

    [Fact]
    public async Task RateLimit_UsesForwardedClientIpWhenProxyIsTrusted()
    {
        var trusted = new Ballast.Core.BallastSettings
        {
            Environment = "development",
            JwtSecret = TestSettings.Secret,
            PasswordIterations = 1000,
            AsOfDate = TestSettings.AsOf,
            AuthRateLimitPerMinute = 3,
            TrustProxy = true
        };
        await using var s = await ApiServer.StartAsync(trusted);
        var distinct = await LoginBurstAsync(s, "10.1.0.1", "10.1.0.2", "10.1.0.3", "10.1.0.4", "10.1.0.5");
        Assert.False(distinct.Contains(HttpStatusCode.TooManyRequests), "different clients behind the proxy get their own bucket: " + string.Join(",", distinct));
        var same = await LoginBurstAsync(s, "10.2.0.9", "10.2.0.9", "10.2.0.9", "10.2.0.9", "10.2.0.9");
        Assert.True(same.Contains(HttpStatusCode.TooManyRequests), "one client is still limited: " + string.Join(",", same));
    }

    [Fact]
    public async Task SecurityHeadersAndNoStoreArePresent()
    {
        var s = await Shared.Server;
        using var res = await s.Http.GetAsync("/health");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").First());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").First());
        var (_, _) = await s.GetAsync("/api/v1/cash/summary");
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cash/summary");
        req.Headers.Authorization = new("Bearer", await s.TokenAsync("treasurer@ballast.example"));
        using var api = await s.Http.SendAsync(req);
        Assert.Contains("no-store", api.Headers.CacheControl?.ToString());
    }
}

public class DataApiTests
{
    [Fact]
    public async Task CashSummary_MatchesAccountsAndContract()
    {
        var s = await Shared.Server;
        var (status, summary) = await s.GetAsync("/api/v1/cash/summary");
        Assert.Equal(200, status);
        Assert.Equal("USD", summary!["baseCurrency"]!.GetValue<string>());
        Assert.Equal(14, summary["accountCount"]!.GetValue<int>());
        var (_, accounts) = await s.GetAsync("/api/v1/cash/accounts");
        double sum = accounts!.AsArray().Sum(a => a!["balanceBase"]!.GetValue<double>());
        Assert.Equal(summary["totalBase"]!.GetValue<double>(), sum, 1.0);
        foreach (var key in new[] { "byCurrency", "byEntity", "byBank" }) Assert.NotNull(summary[key]);
    }

    [Fact]
    public async Task Forecast_HasContractedShapeAndOrderedIntervals()
    {
        var s = await Shared.Server;
        var (status, f) = await s.GetAsync("/api/v1/forecast?scope=all&horizonDays=30&confidence=0.9");
        Assert.Equal(200, status);
        Assert.Equal(30, f!["daily"]!.AsArray().Count);
        Assert.Equal(60, f["history"]!.AsArray().Count);
        Assert.Equal("cash-forecast-ensemble", f["model"]!["name"]!.GetValue<string>());
        foreach (var d in f["daily"]!.AsArray())
        {
            Assert.True(d!["lower"]!.GetValue<double>() <= d["point"]!.GetValue<double>());
            Assert.True(d["point"]!.GetValue<double>() <= d["upper"]!.GetValue<double>());
            Assert.True(d["cumulativeLower"]!.GetValue<double>() <= d["cumulativeUpper"]!.GetValue<double>());
        }
        Assert.Equal("2026-11-05", f["daily"]![0]!["date"]!.GetValue<string>());
    }

    [Fact]
    public async Task Forecast_ScopesAndValidation()
    {
        var s = await Shared.Server;
        var (ok, eur) = await s.GetAsync("/api/v1/forecast?scope=currency:EUR&horizonDays=14");
        Assert.Equal(200, ok);
        Assert.Equal("EUR", eur!["currency"]!.GetValue<string>());
        Assert.Equal(200, (await s.GetAsync("/api/v1/forecast?scope=account:ACC-DE-001&horizonDays=14")).Status);
        Assert.Equal(400, (await s.GetAsync("/api/v1/forecast?scope=banana")).Status);
        Assert.Equal(400, (await s.GetAsync("/api/v1/forecast?horizonDays=3")).Status);
        Assert.Equal(404, (await s.GetAsync("/api/v1/forecast?scope=account:NOPE")).Status);
        Assert.Equal(404, (await s.GetAsync("/api/v1/forecast?scope=account:ACC-US-003")).Status);
    }

    [Fact]
    public async Task Backtest_BeatsSeasonalNaiveOnGroupCash()
    {
        var s = await Shared.Server;
        var (status, m) = await s.GetAsync("/api/v1/forecast/backtest?horizonDays=28");
        Assert.Equal(200, status);
        Assert.True(m!["mase"]!.GetValue<double>() < 1.0, $"MASE {m["mase"]}");
        Assert.True(m["intervalCoverage"]!.GetValue<double>() > 0.6);
    }

    [Fact]
    public async Task Ladder_ProjectsFromCurrentBalance()
    {
        var s = await Shared.Server;
        var (_, ladder) = await s.GetAsync("/api/v1/cash/ladder");
        Assert.Equal(3, ladder!["buckets"]!.AsArray().Count);
        foreach (var b in ladder["buckets"]!.AsArray())
            Assert.True(b!["lower"]!.GetValue<double>() <= b["upper"]!.GetValue<double>());
    }

    [Fact]
    public async Task Transactions_FilterAndLimit()
    {
        var s = await Shared.Server;
        var (_, rows) = await s.GetAsync("/api/v1/cash/transactions?accountId=ACC-DE-001&days=10&limit=5");
        Assert.True(rows!.AsArray().Count is > 0 and <= 5);
        Assert.True(rows.AsArray().All(r => r!["accountId"]!.GetValue<string>() == "ACC-DE-001"));
    }

    [Fact]
    public async Task Fx_ExposuresRatesAndRecommendations()
    {
        var s = await Shared.Server;
        var (_, rates) = await s.GetAsync("/api/v1/fx/rates");
        Assert.Equal(4, rates!.AsArray().Count);
        var (_, exp) = await s.GetAsync("/api/v1/fx/exposures");
        Assert.Equal(12, exp!["lines"]!.AsArray().Count);
        var (_, recs) = await s.GetAsync("/api/v1/fx/recommendations");
        Assert.True(recs!.AsArray().Count > 0);
        foreach (var r in recs.AsArray())
        {
            double target = r!["targetRatio"]!.GetValue<double>();
            Assert.InRange(target, 0.1, 0.9);
            Assert.True(r["recommendedNotional"]!.GetValue<double>() >= 0);
        }
    }

    [Fact]
    public async Task Hedges_BookAndCancelWithRoleChecks()
    {
        var s = await Shared.Server;
        var body = new { currency = "EUR", direction = "SellForeign", notional = 250000, tenorDays = 45, counterparty = "Test Bank" };
        Assert.Equal(403, (await s.PostAsync("/api/v1/fx/hedges", "analyst@ballast.example", body)).Status);
        var (created, hedge) = await s.PostAsync("/api/v1/fx/hedges", "treasurer@ballast.example", body);
        Assert.Equal(201, created);
        string id = hedge!["id"]!.GetValue<string>();
        Assert.Equal(0.0, hedge["markToMarketBase"]!.GetValue<double>(), 1.0);
        Assert.Equal(400, (await s.PostAsync("/api/v1/fx/hedges", "treasurer@ballast.example", new { currency = "USD", direction = "SellForeign", notional = 1, tenorDays = 5 })).Status);
        Assert.Equal(400, (await s.PostAsync("/api/v1/fx/hedges", "treasurer@ballast.example", new { currency = "EUR", direction = "SellForeign", notional = -1, tenorDays = 5 })).Status);
        var (cancelled, c) = await s.PostAsync($"/api/v1/fx/hedges/{id}/cancel", "treasurer@ballast.example");
        Assert.Equal(200, cancelled);
        Assert.Equal("Cancelled", c!["status"]!.GetValue<string>());
        Assert.Equal(409, (await s.PostAsync($"/api/v1/fx/hedges/{id}/cancel", "treasurer@ballast.example")).Status);
    }

    [Fact]
    public async Task Risk_EndpointsReturnConsistentNumbers()
    {
        var s = await Shared.Server;
        var (_, m) = await s.GetAsync("/api/v1/risk/metrics");
        var car = m!["cashAtRisk"]!.AsArray();
        Assert.Equal(4, car.Count);
        Assert.True(car[1]!["cashAtRisk"]!.GetValue<double>() >= car[0]!["cashAtRisk"]!.GetValue<double>(), "99% CaR >= 95% CaR");
        var (_, stress) = await s.GetAsync("/api/v1/risk/stress-scenarios");
        Assert.Equal(5, stress!.AsArray().Count);
        var (_, limits) = await s.GetAsync("/api/v1/risk/limits");
        Assert.True(limits!.AsArray().Any(l => l!["id"]!.GetValue<string>() == "liquidity-buffer"));
    }

    [Fact]
    public async Task Debt_CovenantsMatchSummary()
    {
        var s = await Shared.Server;
        var (_, summary) = await s.GetAsync("/api/v1/debt/summary");
        var (_, cov) = await s.GetAsync("/api/v1/debt/covenants");
        Assert.Equal(summary!["covenants"]!.AsArray().Count, cov!.AsArray().Count);
        Assert.True(cov.AsArray().Any(c => c!["status"]!.GetValue<string>() == "Warning"));
        Assert.Equal(3, summary["facilities"]!.AsArray().Count);
    }

    [Fact]
    public async Task Anomalies_AssessFlagsNewLargeBeneficiaryButNotRoutine()
    {
        var s = await Shared.Server;
        var (_, odd) = await s.PostAsync("/api/v1/anomalies/assess", "analyst@ballast.example",
            new { accountId = "ACC-US-001", beneficiary = "Brand New Vendor LLC", amount = 900000, valueDate = "2026-11-10" });
        Assert.True(odd!["flagged"]!.GetValue<bool>());
        Assert.True(odd["newCounterparty"]!.GetValue<bool>());
        var (_, normal) = await s.PostAsync("/api/v1/anomalies/assess", "analyst@ballast.example",
            new { accountId = "ACC-US-001", beneficiary = "Globex Materials", amount = 20000, valueDate = "2026-11-10" });
        Assert.False(normal!["flagged"]!.GetValue<bool>(), normal.ToJsonString());
        var (_, list) = await s.GetAsync("/api/v1/anomalies");
        Assert.True(list!.AsArray().Any(i => i!["reference"]!.GetValue<string>() == "PAY-1005"));
    }

    [Fact]
    public async Task AiModels_ReportsRuntimeStatus()
    {
        var s = await Shared.Server;
        var (_, ai) = await s.GetAsync("/api/v1/ai/models");
        Assert.Equal(4, ai!["models"]!.AsArray().Count);
        Assert.Equal(25000, ai["hedgingAgentEpisodes"]!.GetValue<int>());
        Assert.Equal(4, ai["regimes"]!.AsArray().Count);
    }

    [Fact]
    public async Task MetaEndpoints_ListRoutesForAuthenticatedUsers()
    {
        var s = await Shared.Server;
        Assert.Equal(401, (await s.GetAsync("/api/v1/meta/endpoints", null)).Status);
        var (status, routes) = await s.GetAsync("/api/v1/meta/endpoints");
        Assert.Equal(200, status);
        Assert.True(routes!.AsArray().Count > 30);
    }
}

public class WorkflowApiTests
{
    private static object Payment(string account, string beneficiary, decimal amount) =>
        new { accountId = account, beneficiary, amount, reference = "TEST", valueDate = "2026-11-09" };

    [Fact]
    public async Task SmallPayment_FullLifecycleWithAudit()
    {
        var s = await Shared.Server;
        var (created, p) = await s.PostAsync("/api/v1/payments", "treasurer@ballast.example", Payment("ACC-US-001", "Globex Materials", 12_345m));
        Assert.Equal(201, created);
        string id = p!["id"]!.GetValue<string>();
        Assert.Equal("Draft", p["status"]!.GetValue<string>());
        Assert.Equal(1, p["requiredApprovals"]!.GetValue<int>());
        Assert.Equal(409, (await s.PostAsync($"/api/v1/payments/{id}/release", "treasurer@ballast.example")).Status);
        Assert.Equal("PendingApproval", (await s.PostAsync($"/api/v1/payments/{id}/submit", "treasurer@ballast.example")).Body!["status"]!.GetValue<string>());
        Assert.Equal(403, (await s.PostAsync($"/api/v1/payments/{id}/approve", "treasurer@ballast.example")).Status);
        Assert.Equal("Approved", (await s.PostAsync($"/api/v1/payments/{id}/approve", "approver@ballast.example")).Body!["status"]!.GetValue<string>());
        Assert.Equal("Released", (await s.PostAsync($"/api/v1/payments/{id}/release", "treasurer@ballast.example")).Body!["status"]!.GetValue<string>());
        var (_, audit) = await s.GetAsync("/api/v1/audit?limit=50");
        var actions = audit!.AsArray().Where(a => a!["subject"]!.GetValue<string>() == id).Select(a => a!["action"]!.GetValue<string>()).ToList();
        foreach (var a in new[] { "payment.create", "payment.submit", "payment.approve", "payment.release" }) Assert.True(actions.Contains(a), a);
        Assert.True((await s.GetAsync("/api/v1/audit/verify")).Body!["valid"]!.GetValue<bool>());
    }

    [Fact]
    public async Task LargePayment_NeedsTwoDistinctApprovers()
    {
        var s = await Shared.Server;
        var (_, p) = await s.PostAsync("/api/v1/payments", "treasurer@ballast.example", Payment("ACC-US-001", "Initech Components", 75_000m));
        string id = p!["id"]!.GetValue<string>();
        Assert.Equal(2, p["requiredApprovals"]!.GetValue<int>());
        await s.PostAsync($"/api/v1/payments/{id}/submit", "treasurer@ballast.example");
        var (_, first) = await s.PostAsync($"/api/v1/payments/{id}/approve", "approver@ballast.example");
        Assert.Equal("PendingApproval", first!["status"]!.GetValue<string>());
        Assert.Equal(409, (await s.PostAsync($"/api/v1/payments/{id}/approve", "approver@ballast.example")).Status);
        var (_, second) = await s.PostAsync($"/api/v1/payments/{id}/approve", "approver2@ballast.example");
        Assert.Equal("Approved", second!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnomalousPayment_IsFlaggedAndEscalated()
    {
        var s = await Shared.Server;
        var (_, p) = await s.PostAsync("/api/v1/payments", "treasurer@ballast.example", Payment("ACC-US-001", "Shell Trading Co", 1_400_000m));
        Assert.Equal(2, p!["requiredApprovals"]!.GetValue<int>());
        Assert.True(p["riskFlags"]!.AsArray().Count > 0);
    }

    [Fact]
    public async Task DuplicatePayment_IsDetected()
    {
        var s = await Shared.Server;
        await s.PostAsync("/api/v1/payments", "treasurer@ballast.example", Payment("ACC-DE-001", "Hooli Cloud", 4_321m));
        var (_, dup) = await s.PostAsync("/api/v1/payments", "treasurer@ballast.example", Payment("ACC-DE-001", "Hooli Cloud", 4_321m));
        Assert.True(dup!["riskFlags"]!.AsArray().Any(f => f!.GetValue<string>().Contains("duplicate", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, dup["requiredApprovals"]!.GetValue<int>());
    }

    [Fact]
    public async Task Payments_RejectInvalidInputAndEnforceRoles()
    {
        var s = await Shared.Server;
        Assert.Equal(403, (await s.PostAsync("/api/v1/payments", "analyst@ballast.example", Payment("ACC-US-001", "X Corp", 100m))).Status);
        Assert.Equal(400, (await s.PostAsync("/api/v1/payments", "treasurer@ballast.example", Payment("ACC-US-001", "X Corp", 0m))).Status);
        Assert.Equal(404, (await s.PostAsync("/api/v1/payments", "treasurer@ballast.example", Payment("ACC-NOPE", "X Corp", 100m))).Status);
        Assert.Equal(404, (await s.GetAsync("/api/v1/payments/PAY-9999")).Status);
        Assert.Equal(400, (await s.GetAsync("/api/v1/payments?status=Bogus")).Status);
        var pastDate = new { accountId = "ACC-US-001", beneficiary = "X Corp", amount = 100m, reference = "", valueDate = "2020-01-01" };
        Assert.Equal(400, (await s.PostAsync("/api/v1/payments", "treasurer@ballast.example", pastDate)).Status);
    }

    [Fact]
    public async Task Reject_RequiresReason()
    {
        var s = await Shared.Server;
        var (_, p) = await s.PostAsync("/api/v1/payments", "treasurer@ballast.example", Payment("ACC-UK-001", "Wingtip Online", 2_222m));
        string id = p!["id"]!.GetValue<string>();
        await s.PostAsync($"/api/v1/payments/{id}/submit", "treasurer@ballast.example");
        Assert.Equal(400, (await s.PostAsync($"/api/v1/payments/{id}/reject", "approver@ballast.example", new { reason = " " })).Status);
        var (status, rejected) = await s.PostAsync($"/api/v1/payments/{id}/reject", "approver@ballast.example", new { reason = "Wrong amount" });
        Assert.Equal(200, status);
        Assert.Equal("Rejected", rejected!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Netting_PreviewThenRunClearsBalances()
    {
        await using var s = await ApiServer.StartAsync();
        var (_, preview) = await s.GetAsync("/api/v1/inhouse-bank/netting/preview");
        Assert.True(preview!["netVolume"]!.GetValue<double>() < preview["grossVolume"]!.GetValue<double>());
        Assert.Equal(403, (await s.PostAsync("/api/v1/inhouse-bank/netting/run", "analyst@ballast.example")).Status);
        var (status, cycle) = await s.PostAsync("/api/v1/inhouse-bank/netting/run", "treasurer@ballast.example", new { settlementCurrency = "EUR" });
        Assert.Equal(201, status);
        Assert.Equal("EUR", cycle!["settlementCurrency"]!.GetValue<string>());
        var (_, balances) = await s.GetAsync("/api/v1/inhouse-bank/balances");
        Assert.Equal(0, balances!.AsArray().Count);
        Assert.Equal(409, (await s.PostAsync("/api/v1/inhouse-bank/netting/run", "treasurer@ballast.example")).Status);
        Assert.Equal(1, (await s.GetAsync("/api/v1/inhouse-bank/netting/cycles")).Body!.AsArray().Count);
        Assert.Equal(400, (await s.GetAsync("/api/v1/inhouse-bank/netting/preview?settlementCurrency=XYZ")).Status);
    }
}

public class PersistenceTests
{
    [Fact]
    public async Task State_SurvivesRestartAndAuditChainStaysValid()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ballast-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string paymentId;
            await using (var first = await ApiServer.StartAsync(TestSettings.Create(dir)))
            {
                var reg = await first.Http.PostAsJsonAsync("/api/auth/register",
                    new { email = "persist@example.com", password = "Persist-Passw0rd!", name = "Persist User" });
                Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
                var (_, p) = await first.PostAsync("/api/v1/payments", "treasurer@ballast.example",
                    new { accountId = "ACC-US-001", beneficiary = "Stark Freight", amount = 3_210m, reference = "P", valueDate = "2026-11-09" });
                paymentId = p!["id"]!.GetValue<string>();
                await first.PostAsync("/api/v1/inhouse-bank/netting/run", "treasurer@ballast.example");
            }
            Assert.True(File.Exists(Path.Combine(dir, "ballast-state.json")));
            await using var second = await ApiServer.StartAsync(TestSettings.Create(dir));
            var login = await second.Http.PostAsJsonAsync("/api/auth/login", new { email = "persist@example.com", password = "Persist-Passw0rd!" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Equal(200, (await second.GetAsync($"/api/v1/payments/{paymentId}")).Status);
            Assert.Equal(0, (await second.GetAsync("/api/v1/inhouse-bank/balances")).Body!.AsArray().Count);
            var verify = (await second.GetAsync("/api/v1/audit/verify")).Body!;
            Assert.True(verify["valid"]!.GetValue<bool>(), verify.ToJsonString());
            Assert.True(verify["entries"]!.GetValue<int>() > 3);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ProductionLikeStartup_RefusesWeakSecretAndSeedsNoDemoUsers()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ApiServer.StartAsync(new Ballast.Core.BallastSettings { Environment = "production", JwtSecret = "weak" }));
        await using var s = await ApiServer.StartAsync(new Ballast.Core.BallastSettings
        {
            Environment = "production",
            JwtSecret = new string('p', 48),
            PasswordIterations = 100_000,
            AsOfDate = TestSettings.AsOf
        });
        var res = await s.Http.PostAsJsonAsync("/api/auth/login", new { email = "admin@ballast.example", password = TestSettings.DemoPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var reg = await s.Http.PostAsJsonAsync("/api/auth/register", new { email = "first@example.com", password = "First-Passw0rd!!", name = "First Admin" });
        Assert.Equal("Admin", JsonNode.Parse(await reg.Content.ReadAsStringAsync())!["user"]!["role"]!.GetValue<string>());
    }

    [Fact]
    public async Task ProductionLike_DisablesSelfRegistrationAfterBootstrapAndAdminsCreateUsers()
    {
        await using var s = await ApiServer.StartAsync(new Ballast.Core.BallastSettings
        {
            Environment = "production",
            JwtSecret = new string('p', 48),
            PasswordIterations = 100_000,
            AsOfDate = TestSettings.AsOf
        });
        var first = await s.Http.PostAsJsonAsync("/api/auth/register", new { email = "root@example.com", password = "Root-Passw0rd!!!", name = "Root Admin" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await s.Http.PostAsJsonAsync("/api/auth/register", new { email = "intruder@example.com", password = "Intruder-Passw0rd!", name = "Intruder" });
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);

        var login = await s.Http.PostAsJsonAsync("/api/auth/login", new { email = "root@example.com", password = "Root-Passw0rd!!!" });
        var token = JsonNode.Parse(await login.Content.ReadAsStringAsync())!["accessToken"]!.GetValue<string>();
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/auth/users")
        {
            Content = JsonContent.Create(new { email = "treasury@example.com", name = "Tess Treasurer", password = "Treasury-Passw0rd!", role = "Treasurer" })
        };
        create.Headers.Authorization = new("Bearer", token);
        using var created = await s.Http.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ok = await s.Http.PostAsJsonAsync("/api/auth/login", new { email = "treasury@example.com", password = "Treasury-Passw0rd!" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }
}
