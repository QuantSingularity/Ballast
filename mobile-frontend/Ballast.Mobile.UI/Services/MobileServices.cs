using Ballast.Mobile.UI.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ballast.Mobile.UI.Services;

public sealed class MobileOptions
{
    public string ApiBaseAddress { get; set; } = "http://localhost:5000";
}

public static class MobileServiceCollectionExtensions
{
    public static IServiceCollection AddBallastMobile(this IServiceCollection services, Action<MobileOptions>? configure = null)
    {
        var options = new MobileOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.TryAddSingleton<ITokenStore, InMemoryTokenStore>();
        services.AddSingleton(sp => new ApiClient(
            new HttpClient { BaseAddress = new Uri(options.ApiBaseAddress), Timeout = TimeSpan.FromSeconds(30) },
            sp.GetRequiredService<ITokenStore>()));
        services.AddTransient<ApprovalsViewModel>();
        return services;
    }
}

public abstract class ApiPageBase : ComponentBase
{
    [Inject] protected ApiClient Api { get; set; } = default!;

    [Inject] protected NavigationManager Nav { get; set; } = default!;

    protected bool Loading { get; private set; } = true;

    protected string? Error { get; private set; }

    protected async Task RunAsync(Func<Task> action)
    {
        Error = null;
        try
        {
            await Api.InitializeAsync();
            await action();
        }
        catch (ApiException ex) when (ex.Status == 401)
        {
            Nav.NavigateTo("login");
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Loading = false;
        }
    }
}

public sealed class ApprovalsViewModel(ApiClient api)
{
    public IReadOnlyList<Payment> Pending { get; private set; } = [];

    public string? Error { get; private set; }

    public string? Notice { get; private set; }

    public bool Busy { get; private set; }

    public bool CanApprove(Payment p) => ApprovalRules.CanApprove(p, api.User);

    public bool CanReject(Payment p) => ApprovalRules.CanReject(p, api.User);

    public async Task LoadAsync()
    {
        await RunAsync(async () => Pending = await api.GetPaymentsAsync("PendingApproval"), reload: false);
    }

    public Task ApproveAsync(Payment p) => RunAsync(async () =>
    {
        if (!CanApprove(p)) throw new ApiException("forbidden", "You cannot approve this payment", 403);
        var updated = await api.ApprovePaymentAsync(p.Id);
        Notice = updated.Status == "Approved" ? $"{updated.Id} fully approved" : $"{updated.Id} approved, {updated.Approvals.Count} of {updated.RequiredApprovals} approvals";
    });

    public Task RejectAsync(Payment p, string reason) => RunAsync(async () =>
    {
        if (!CanReject(p)) throw new ApiException("forbidden", "You cannot reject this payment", 403);
        if (string.IsNullOrWhiteSpace(reason)) throw new ApiException("validation_error", "A rejection reason is required", 400);
        await api.RejectPaymentAsync(p.Id, reason.Trim());
        Notice = $"{p.Id} rejected";
    });

    private async Task RunAsync(Func<Task> action, bool reload = true)
    {
        Busy = true;
        Error = null;
        Notice = null;
        try
        {
            await action();
            if (reload) Pending = await api.GetPaymentsAsync("PendingApproval");
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }
}

public static class ApprovalRules
{
    public static bool IsChecker(UserInfo? user) => user is { Role: "Approver" or "Admin" };

    public static bool CanApprove(Payment p, UserInfo? user) =>
        user is not null && IsChecker(user) && p.Status == "PendingApproval" && p.CreatedBy != user.Id
        && !p.Approvals.Any(a => a.UserId == user.Id);

    public static bool CanReject(Payment p, UserInfo? user) =>
        user is not null && IsChecker(user) && p.Status == "PendingApproval";
}
