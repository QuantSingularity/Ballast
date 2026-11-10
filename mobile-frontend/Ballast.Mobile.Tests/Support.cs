using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Ballast.Core;
using Ballast.Mobile.UI.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ballast.Mobile.Tests;

public static class Demo
{
    public const string Password = "Ballast-Demo-2026!";
    public const string Treasurer = "treasurer@ballast.example";
    public const string Approver = "approver@ballast.example";
    public const string Approver2 = "approver2@ballast.example";
}

public sealed class TestNavigationManager : NavigationManager
{
    public TestNavigationManager() => Initialize("http://localhost/", "http://localhost/");

    public List<string> Navigations { get; } = [];

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        Navigations.Add(uri);
        Uri = ToAbsoluteUri(uri).ToString();
    }
}

public sealed class ApiServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ApiServer(WebApplication app, string url)
    {
        _app = app;
        Url = url;
    }

    public string Url { get; }

    public static async Task<ApiServer> StartAsync()
    {
        var settings = new BallastSettings
        {
            Environment = "development",
            JwtSecret = "mobile-test-secret-key-0123456789-abcdef",
            PasswordIterations = 1000,
            AsOfDate = new DateOnly(2026, 11, 5),
            AuthRateLimitPerMinute = 10_000,
            AiServiceUrl = Ballast.TestKit.AiServiceProcess.Url
        };
        var app = Ballast.Api.ApiHost.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=Warning"], settings);
        await app.StartAsync();
        return new ApiServer(app, app.Urls.First());
    }

    public async Task<string> TokenAsync(string email)
    {
        using var http = new HttpClient { BaseAddress = new Uri(Url) };
        var res = await http.PostAsJsonAsync("/api/auth/login", new { email, password = Demo.Password });
        res.EnsureSuccessStatusCode();
        return JsonNode.Parse(await res.Content.ReadAsStringAsync())!["accessToken"]!.GetValue<string>();
    }

    public async Task<JsonNode> PostAsync(string email, string path, object body)
    {
        using var http = new HttpClient { BaseAddress = new Uri(Url) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(email));
        var res = await http.PostAsJsonAsync(path, body);
        res.EnsureSuccessStatusCode();
        return JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
    }

    public async Task<string> CreatePendingPaymentAsync(decimal amount, string beneficiary)
    {
        var p = await PostAsync(Demo.Treasurer, "/api/v1/payments",
            new { accountId = "ACC-US-001", beneficiary, amount, reference = "MOBILE", valueDate = "2026-11-09" });
        string id = p["id"]!.GetValue<string>();
        await PostAsync(Demo.Treasurer, $"/api/v1/payments/{id}/submit", new { });
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public static class Shared
{
    private static readonly Lazy<Task<ApiServer>> Lazy = new(ApiServer.StartAsync);

    public static Task<ApiServer> Server => Lazy.Value;

    public static ServiceProvider Services(ApiServer server, TestNavigationManager? nav = null, ITokenStore? store = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<NavigationManager>(nav ?? new TestNavigationManager());
        if (store is not null) services.AddSingleton(store);
        services.AddBallastMobile(o => o.ApiBaseAddress = server.Url);
        return services.BuildServiceProvider();
    }

    public static async Task<(ServiceProvider Sp, ApiClient Api, TestNavigationManager Nav)> SignedInAsync(string email)
    {
        var server = await Server;
        var nav = new TestNavigationManager();
        var sp = Services(server, nav);
        var api = sp.GetRequiredService<ApiClient>();
        await api.LoginAsync(email, Demo.Password);
        return (sp, api, nav);
    }

    public static async Task<string> RenderAsync<T>(IServiceProvider sp, Dictionary<string, object?>? parameters = null) where T : IComponent
    {
        await using var renderer = new HtmlRenderer(sp, sp.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? []))).ToHtmlString());
    }
}
