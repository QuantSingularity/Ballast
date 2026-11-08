using Ballast.Mobile.UI.Services;
using Ballast.TestKit;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Mobile.Tests;

public class ApiClientTests
{
    [Fact]
    public async Task Login_StoresSessionAndExposesUser()
    {
        var server = await Shared.Server;
        var store = new InMemoryTokenStore();
        var sp = Shared.Services(server, store: store);
        var api = sp.GetRequiredService<ApiClient>();
        var user = await api.LoginAsync(Demo.Approver, Demo.Password);
        Assert.Equal("Approver", user.Role);
        Assert.True(api.IsAuthenticated);
        var stored = await store.LoadAsync();
        Assert.NotNull(stored);
        Assert.Equal(3, stored!.AccessToken.Split('.').Length);
    }

    [Fact]
    public async Task Login_WithBadPasswordThrowsReadableApiException()
    {
        var server = await Shared.Server;
        var api = Shared.Services(server).GetRequiredService<ApiClient>();
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.LoginAsync(Demo.Approver, "wrong"));
        Assert.Equal(401, ex.Status);
        Assert.Equal("unauthorized", ex.Code);
        Assert.Contains("Invalid email or password", ex.Message);
        Assert.False(api.IsAuthenticated);
    }

    [Fact]
    public async Task UnreachableServer_IsReportedAsNetworkError()
    {
        var sp = new ServiceCollection().AddBallastMobile(o => o.ApiBaseAddress = "http://127.0.0.1:1").BuildServiceProvider();
        var ex = await Assert.ThrowsAsync<ApiException>(() => sp.GetRequiredService<ApiClient>().LoginAsync("a@b.co", "x"));
        Assert.Equal("network", ex.Code);
    }

    [Fact]
    public async Task Requests_WithoutASession_AreUnauthorized()
    {
        var server = await Shared.Server;
        var api = Shared.Services(server).GetRequiredService<ApiClient>();
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.GetCashSummaryAsync());
        Assert.Equal(401, ex.Status);
    }

    [Fact]
    public async Task Data_EndpointsDeserializeIntoMobileModels()
    {
        var (_, api, _) = await Shared.SignedInAsync(Demo.Approver);
        var summary = await api.GetCashSummaryAsync();
        Assert.Equal("USD", summary.BaseCurrency);
        Assert.Equal(14, summary.AccountCount);
        Assert.True(summary.TotalBase > 1_000_000m);
        var forecast = await api.GetForecastAsync("all", 30);
        Assert.Equal(30, forecast.Daily.Count);
        Assert.True(forecast.Daily[^1].CumulativeLower <= forecast.Daily[^1].CumulativeUpper);
        Assert.True((await api.GetLimitsAsync()).Count > 5);
        Assert.True((await api.GetCovenantsAsync()).Any(c => c.Status == "Warning"));
        Assert.Equal(12, (await api.GetExposuresAsync()).Lines.Count);
        Assert.True((await api.GetRiskMetricsAsync()).LiquidityCoverageRatio > 0);
        Assert.True((await api.GetLadderAsync()).Buckets.Count == 3);
        Assert.True((await api.GetRecommendationsAsync()).Count > 0);
        Assert.True((await api.GetAnomaliesAsync()).Any(a => a.Reference == "PAY-1005"));
    }

    [Fact]
    public async Task ExpiredAccessToken_IsRefreshedTransparently()
    {
        var server = await Shared.Server;
        var store = new InMemoryTokenStore();
        var first = Shared.Services(server, store: store).GetRequiredService<ApiClient>();
        await first.LoginAsync(Demo.Approver, Demo.Password);
        var good = (await store.LoadAsync())!;
        await store.SaveAsync(good with { AccessToken = "not.a.valid-token" });

        var second = Shared.Services(server, store: store).GetRequiredService<ApiClient>();
        var summary = await second.GetCashSummaryAsync();
        Assert.True(summary.TotalBase > 0);
        var refreshed = (await store.LoadAsync())!;
        Assert.True(refreshed.AccessToken != "not.a.valid-token");
    }

    [Fact]
    public async Task BothTokensInvalid_ClearsSessionAndAsksToSignInAgain()
    {
        var server = await Shared.Server;
        var store = new InMemoryTokenStore();
        var login = Shared.Services(server, store: store).GetRequiredService<ApiClient>();
        await login.LoginAsync(Demo.Approver, Demo.Password);
        var good = (await store.LoadAsync())!;
        await store.SaveAsync(good with { AccessToken = "bad", RefreshToken = "also-bad" });

        var api = Shared.Services(server, store: store).GetRequiredService<ApiClient>();
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.GetCashSummaryAsync());
        Assert.Equal(401, ex.Status);
        Assert.Contains("sign in again", ex.Message);
        Assert.False(api.IsAuthenticated);
        Assert.True(await store.LoadAsync() is null);
    }

    [Fact]
    public async Task Logout_ClearsTheStoreAndRaisesSessionChanged()
    {
        var server = await Shared.Server;
        var store = new InMemoryTokenStore();
        var api = Shared.Services(server, store: store).GetRequiredService<ApiClient>();
        await api.LoginAsync(Demo.Approver, Demo.Password);
        int raised = 0;
        api.SessionChanged += () => raised++;
        await api.LogoutAsync();
        Assert.Equal(1, raised);
        Assert.False(api.IsAuthenticated);
        Assert.True(await store.LoadAsync() is null);
    }
}

public class ApprovalsViewModelTests
{
    [Fact]
    public async Task Approver_ApprovesSingleApprovalPaymentEndToEnd()
    {
        var server = await Shared.Server;
        string id = await server.CreatePendingPaymentAsync(2_468m, "Wingtip Online");
        var (sp, _, _) = await Shared.SignedInAsync(Demo.Approver);
        var vm = sp.GetRequiredService<ApprovalsViewModel>();
        await vm.LoadAsync();
        var p = vm.Pending.First(x => x.Id == id);
        Assert.True(vm.CanApprove(p));
        await vm.ApproveAsync(p);
        Assert.Contains("fully approved", vm.Notice);
        Assert.True(vm.Pending.All(x => x.Id != id), "approved payment leaves the pending list");
        Assert.True(vm.Error is null);
    }

    [Fact]
    public async Task DualApprovalPayment_StaysPendingUntilSecondApprover()
    {
        var server = await Shared.Server;
        string id = await server.CreatePendingPaymentAsync(88_000m, "Initech Components");
        var (sp1, _, _) = await Shared.SignedInAsync(Demo.Approver);
        var vm1 = sp1.GetRequiredService<ApprovalsViewModel>();
        await vm1.LoadAsync();
        await vm1.ApproveAsync(vm1.Pending.First(x => x.Id == id));
        Assert.Contains("1 of 2", vm1.Notice);
        var after = vm1.Pending.First(x => x.Id == id);
        Assert.False(vm1.CanApprove(after), "same approver cannot approve twice");

        var (sp2, _, _) = await Shared.SignedInAsync(Demo.Approver2);
        var vm2 = sp2.GetRequiredService<ApprovalsViewModel>();
        await vm2.LoadAsync();
        await vm2.ApproveAsync(vm2.Pending.First(x => x.Id == id));
        Assert.Contains("fully approved", vm2.Notice);
    }

    [Fact]
    public async Task Treasurer_CannotApproveAndNoRequestIsSent()
    {
        var server = await Shared.Server;
        string id = await server.CreatePendingPaymentAsync(1_357m, "Hooli Cloud");
        var (sp, api, _) = await Shared.SignedInAsync(Demo.Treasurer);
        var vm = sp.GetRequiredService<ApprovalsViewModel>();
        await vm.LoadAsync();
        var p = vm.Pending.First(x => x.Id == id);
        Assert.False(vm.CanApprove(p));
        await vm.ApproveAsync(p);
        Assert.Contains("cannot approve", vm.Error);
        var still = (await api.GetPaymentsAsync("PendingApproval")).Any(x => x.Id == id);
        Assert.True(still);
    }

    [Fact]
    public async Task Reject_NeedsAReasonThenSucceeds()
    {
        var server = await Shared.Server;
        string id = await server.CreatePendingPaymentAsync(1_111m, "Umbrella Packaging");
        var (sp, _, _) = await Shared.SignedInAsync(Demo.Approver);
        var vm = sp.GetRequiredService<ApprovalsViewModel>();
        await vm.LoadAsync();
        var p = vm.Pending.First(x => x.Id == id);
        await vm.RejectAsync(p, "  ");
        Assert.Contains("reason is required", vm.Error);
        await vm.RejectAsync(p, "Wrong beneficiary account");
        Assert.True(vm.Error is null);
        Assert.Contains("rejected", vm.Notice);
        Assert.True(vm.Pending.All(x => x.Id != id));
    }
}
