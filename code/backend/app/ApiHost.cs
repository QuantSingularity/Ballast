using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Ballast.AiClient;
using Ballast.Api.Dependencies;
using Ballast.Api.Schemas;
using Ballast.Api.Services;
using Ballast.Api.V1;
using Ballast.Core;
using Ballast.Core.Domain;
using Ballast.Data;
using Ballast.Infrastructure;
using Ballast.MarketData;
using Ballast.Treasury;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;

namespace Ballast.Api;

public static class ApiHost
{
    public static WebApplication Build(string[] args, BallastSettings? overrides = null, TimeProvider? clock = null)
    {
        var settings = overrides ?? BallastSettings.FromEnvironment(Environment.GetEnvironmentVariable);
        settings.Validate();
        var builder = WebApplication.CreateBuilder(args);

        var services = builder.Services;
        services.AddSingleton(settings);
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddSingleton(new BallastDb(settings.DataPath));
        services.AddSingleton<IRateProvider>(new SyntheticRateProvider(settings.Today));
        services.AddSingleton(new PasswordHasher(settings.PasswordIterations));
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<AuditLog>();
        services.AddSingleton<PaymentWorkflow>();
        services.AddSingleton(new AiServiceClient(new HttpClient
        {
            BaseAddress = new Uri(settings.AiServiceUrl),
            Timeout = TimeSpan.FromSeconds(180)
        }));
        services.AddSingleton<ModelHub>();
        services.AddSingleton<ForecastService>();
        services.AddSingleton<CashService>();
        services.AddSingleton<FxService>();
        services.AddSingleton<DebtService>();
        services.AddSingleton<RiskService>();
        services.AddSingleton<PaymentService>();
        services.AddSingleton<NettingService>();
        services.AddSingleton<AuthService>();

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddAuthentication(BearerAuthHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, BearerAuthHandler>(BearerAuthHandler.SchemeName, null);
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Maker, p => p.RequireRole(nameof(UserRole.Treasurer), nameof(UserRole.Admin)))
            .AddPolicy(Policies.Checker, p => p.RequireRole(nameof(UserRole.Approver), nameof(UserRole.Admin)))
            .AddPolicy(Policies.AdminOnly, p => p.RequireRole(nameof(UserRole.Admin)));
        services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(settings.CorsOrigins).AllowAnyHeader().AllowAnyMethod()));
        services.AddResponseCompression();
        if (settings.TrustProxy)
        {
            services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
            });
        }
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = settings.AuthRateLimitPerMinute, Window = TimeSpan.FromMinutes(1) }));
        });

        var app = builder.Build();
        Initialize(app, settings);

        if (settings.TrustProxy) app.UseForwardedHeaders();
        app.UseResponseCompression();
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["X-Frame-Options"] = "DENY";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
            if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.Headers.CacheControl = "no-store";
            await next();
        });
        app.Use(async (ctx, next) =>
        {
            try { await next(); }
            catch (BallastException ex) when (!ctx.Response.HasStarted)
            {
                ctx.Response.StatusCode = ex.StatusCode;
                await ctx.Response.WriteAsJsonAsync(new ErrorBody(ex.Code, ex.Message));
            }
            catch (Exception ex) when (!ctx.Response.HasStarted)
            {
                app.Logger.LogError(ex, "Unhandled exception for {Path}", ctx.Request.Path);
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await ctx.Response.WriteAsJsonAsync(new ErrorBody("internal_error", "An unexpected error occurred"));
            }
        });
        app.UseCors();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        MetaEndpoints.Map(app, settings);
        AuthEndpoints.Map(app);
        var v1 = app.MapGroup("/api/v1").RequireAuthorization();
        CashEndpoints.Map(v1);
        ForecastEndpoints.Map(v1);
        FxEndpoints.Map(v1);
        RiskEndpoints.Map(v1);
        InHouseBankEndpoints.Map(v1);
        DebtEndpoints.Map(v1);
        PaymentEndpoints.Map(v1);
        AnomalyEndpoints.Map(v1);
        AuditEndpoints.Map(v1);
        AiEndpoints.Map(v1);
        return app;
    }

    private static void Initialize(WebApplication app, BallastSettings settings)
    {
        var db = app.Services.GetRequiredService<BallastDb>();
        var audit = app.Services.GetRequiredService<AuditLog>();
        var hasher = app.Services.GetRequiredService<PasswordHasher>();
        if (settings.SeedDemoData) DemoSeeder.SeedStatic(db, settings);
        bool restored = db.TryLoadSnapshot();
        if (!restored && settings.SeedDemoData)
        {
            DemoSeeder.SeedMutable(db, settings, hasher.Hash);
            audit.Append("system", "system.seed", "demo-data", "Demo data seeded");
            db.SaveChanges();
        }
    }
}
