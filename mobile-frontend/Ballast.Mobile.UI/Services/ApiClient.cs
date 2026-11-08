using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ballast.Mobile.UI.Models;

namespace Ballast.Mobile.UI.Services;

public sealed record StoredSession(UserInfo User, string AccessToken, string RefreshToken);

public interface ITokenStore
{
    Task<StoredSession?> LoadAsync();

    Task SaveAsync(StoredSession session);

    Task ClearAsync();
}

public sealed class InMemoryTokenStore : ITokenStore
{
    private StoredSession? _session;

    public Task<StoredSession?> LoadAsync() => Task.FromResult(_session);

    public Task SaveAsync(StoredSession session)
    {
        _session = session;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _session = null;
        return Task.CompletedTask;
    }
}

public sealed class ApiException(string code, string message, int status) : Exception(message)
{
    public string Code { get; } = code;

    public int Status { get; } = status;
}

public sealed class ApiClient(HttpClient http, ITokenStore store)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private StoredSession? _session;
    private bool _initialized;

    public event Action? SessionChanged;

    public UserInfo? User => _session?.User;

    public bool IsAuthenticated => _session is not null;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _session = await store.LoadAsync();
        _initialized = true;
    }

    public async Task<UserInfo> LoginAsync(string email, string password)
    {
        await InitializeAsync();
        var res = await SendOnceAsync(HttpMethod.Post, "/api/auth/login", new { email, password }, null);
        var auth = await ReadAsync<AuthResponse>(res);
        await SetSessionAsync(new StoredSession(auth.User, auth.AccessToken, auth.RefreshToken));
        return auth.User;
    }

    public async Task LogoutAsync()
    {
        _session = null;
        _initialized = true;
        await store.ClearAsync();
        SessionChanged?.Invoke();
    }

    public Task<CashSummary> GetCashSummaryAsync() => GetAsync<CashSummary>("/api/v1/cash/summary");

    public Task<Ladder> GetLadderAsync() => GetAsync<Ladder>("/api/v1/cash/ladder");

    public Task<RiskMetrics> GetRiskMetricsAsync() => GetAsync<RiskMetrics>("/api/v1/risk/metrics");

    public Task<List<LimitResult>> GetLimitsAsync() => GetAsync<List<LimitResult>>("/api/v1/risk/limits");

    public Task<List<CovenantResult>> GetCovenantsAsync() => GetAsync<List<CovenantResult>>("/api/v1/debt/covenants");

    public Task<List<AnomalyItem>> GetAnomaliesAsync() => GetAsync<List<AnomalyItem>>("/api/v1/anomalies");

    public Task<Exposure> GetExposuresAsync() => GetAsync<Exposure>("/api/v1/fx/exposures");

    public Task<List<HedgeRecommendation>> GetRecommendationsAsync() => GetAsync<List<HedgeRecommendation>>("/api/v1/fx/recommendations");

    public Task<Forecast> GetForecastAsync(string scope = "all", int horizonDays = 91, double confidence = 0.9) =>
        GetAsync<Forecast>($"/api/v1/forecast?scope={Uri.EscapeDataString(scope)}&horizonDays={horizonDays}&confidence={confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    public Task<List<Payment>> GetPaymentsAsync(string? status = null) =>
        GetAsync<List<Payment>>(status is null ? "/api/v1/payments" : $"/api/v1/payments?status={Uri.EscapeDataString(status)}");

    public Task<Payment> ApprovePaymentAsync(string id) => PostAsync<Payment>($"/api/v1/payments/{Uri.EscapeDataString(id)}/approve", new { });

    public Task<Payment> RejectPaymentAsync(string id, string reason) =>
        PostAsync<Payment>($"/api/v1/payments/{Uri.EscapeDataString(id)}/reject", new { reason });

    private Task<T> GetAsync<T>(string path) => RequestAsync<T>(HttpMethod.Get, path, null);

    private Task<T> PostAsync<T>(string path, object body) => RequestAsync<T>(HttpMethod.Post, path, body);

    private async Task<T> RequestAsync<T>(HttpMethod method, string path, object? body)
    {
        await InitializeAsync();
        var res = await SendOnceAsync(method, path, body, _session?.AccessToken);
        if (res.StatusCode == HttpStatusCode.Unauthorized && _session is not null)
        {
            var used = _session.AccessToken;
            res.Dispose();
            if (!await RefreshAsync(used))
            {
                await LogoutAsync();
                throw new ApiException("unauthorized", "Your session has expired. Please sign in again", 401);
            }
            res = await SendOnceAsync(method, path, body, _session!.AccessToken);
        }
        return await ReadAsync<T>(res);
    }

    private async Task<bool> RefreshAsync(string tokenUsed)
    {
        await _refreshLock.WaitAsync();
        try
        {
            if (_session is null) return false;
            if (_session.AccessToken != tokenUsed) return true;
            var res = await SendOnceAsync(HttpMethod.Post, "/api/auth/refresh", new { refreshToken = _session.RefreshToken }, null);
            if (!res.IsSuccessStatusCode)
            {
                res.Dispose();
                return false;
            }
            var auth = await ReadAsync<AuthResponse>(res);
            await SetSessionAsync(new StoredSession(auth.User, auth.AccessToken, auth.RefreshToken));
            return true;
        }
        catch (ApiException)
        {
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task SetSessionAsync(StoredSession session)
    {
        _session = session;
        await store.SaveAsync(session);
        SessionChanged?.Invoke();
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body, string? token)
    {
        using var req = new HttpRequestMessage(method, path);
        if (token is not null) req.Headers.Authorization = new("Bearer", token);
        if (body is not null) req.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        try
        {
            return await http.SendAsync(req);
        }
        catch (HttpRequestException)
        {
            throw new ApiException("network", "Could not reach the server. Check your connection", 0);
        }
        catch (TaskCanceledException)
        {
            throw new ApiException("timeout", "The server took too long to respond", 0);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage res)
    {
        using (res)
        {
            if (res.IsSuccessStatusCode)
            {
                var value = await res.Content.ReadFromJsonAsync<T>(Json);
                return value ?? throw new ApiException("empty_response", "The server returned an empty response", (int)res.StatusCode);
            }
            ErrorBody? error = null;
            try
            {
                error = await res.Content.ReadFromJsonAsync<ErrorBody>(Json);
            }
            catch (JsonException)
            {
            }
            throw new ApiException(error?.Code ?? "http_error", error?.Message ?? $"Request failed with status {(int)res.StatusCode}", (int)res.StatusCode);
        }
    }
}
