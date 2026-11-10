using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Ballast.Api;
using Ballast.Core;
using Ballast.TestKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Backend.Tests;

public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

public static class TestSettings
{
    public static readonly DateOnly AsOf = new(2026, 11, 5);
    public const string Secret = "test-secret-key-test-secret-key-0123456789";
    public const string DemoPassword = "Ballast-Demo-2026!";

    public static BallastSettings Create(string? dataPath = null, int authRateLimit = 1000, int iterations = 1000) => new()
    {
        Environment = "development",
        JwtSecret = Secret,
        PasswordIterations = iterations,
        AsOfDate = AsOf,
        DataPath = dataPath,
        AuthRateLimitPerMinute = authRateLimit,
        DemoPassword = DemoPassword,
        AiServiceUrl = AiServiceProcess.Url
    };
}

public sealed class ApiServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Dictionary<string, string> _tokens = [];

    public HttpClient Http { get; }
    public string BaseUrl { get; }

    private ApiServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
        Http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public static async Task<ApiServer> StartAsync(BallastSettings? settings = null, TimeProvider? clock = null)
    {
        var app = ApiHost.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=Warning"], settings ?? TestSettings.Create(), clock);
        await app.StartAsync();
        return new ApiServer(app, app.Urls.First());
    }

    public T Service<T>() where T : notnull => _app.Services.GetRequiredService<T>();

    public async Task<string> TokenAsync(string email, string password = TestSettings.DemoPassword)
    {
        if (_tokens.TryGetValue(email, out var t)) return t;
        var res = await Http.PostAsJsonAsync("/api/auth/login", new { email, password });
        res.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
        return _tokens[email] = json["accessToken"]!.GetValue<string>();
    }

    public async Task<(int Status, JsonNode? Body)> SendAsync(HttpMethod method, string path, string? email = null, object? body = null)
    {
        using var req = new HttpRequestMessage(method, path);
        if (email is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(email));
        if (body is not null) req.Content = JsonContent.Create(body);
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        return ((int)res.StatusCode, string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text));
    }

    public Task<(int Status, JsonNode? Body)> GetAsync(string path, string? email = "treasurer@ballast.example") =>
        SendAsync(HttpMethod.Get, path, email);

    public Task<(int Status, JsonNode? Body)> PostAsync(string path, string? email, object? body = null) =>
        SendAsync(HttpMethod.Post, path, email, body ?? new { });

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public static class Shared
{
    private static readonly Lazy<Task<ApiServer>> Lazy = new(() => ApiServer.StartAsync());

    public static Task<ApiServer> Server => Lazy.Value;
}
