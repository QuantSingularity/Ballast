using Ballast.Api.Dependencies;
using Ballast.Api.Schemas;
using Ballast.Api.Services;
using Ballast.AiModels.Research;
using Ballast.Core;
using Ballast.Core.Domain;
using Ballast.Data;
using Ballast.Infrastructure;
using Ballast.MarketData;
using Ballast.Risk;
using Ballast.Treasury;

namespace Ballast.Api.V1;

public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/auth").RequireRateLimiting("auth");
        g.MapPost("/register", (RegisterRequest r, AuthService s) => Results.Created("/api/auth/profile", s.Register(r)));
        g.MapPost("/login", (LoginRequest r, AuthService s) => Results.Ok(s.Login(r)));
        g.MapPost("/refresh", (RefreshRequest r, AuthService s) => Results.Ok(s.Refresh(r)));
        g.MapGet("/profile", (HttpContext c, AuthService s) => Results.Ok(s.Profile(c.User.ToActor().Id))).RequireAuthorization();
        g.MapGet("/users", (AuthService s) => Results.Ok(s.Users())).RequireAuthorization(Policies.AdminOnly);
        g.MapPost("/users", (CreateUserRequest r, HttpContext c, AuthService s) =>
            Results.Created("/api/auth/users", s.CreateUser(r, c.User.ToActor()))).RequireAuthorization(Policies.AdminOnly);
        g.MapPatch("/users/{id}/role", (string id, RoleChangeRequest r, HttpContext c, AuthService s) =>
            Results.Ok(s.ChangeRole(id, r.Role, c.User.ToActor()))).RequireAuthorization(Policies.AdminOnly);
    }
}

public static class CashEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/cash");
        g.MapGet("/summary", (CashService s) => s.Summary());
        g.MapGet("/accounts", (CashService s) => s.Accounts());
        g.MapGet("/ladder", (CashService s) => s.Ladder());
        g.MapGet("/transactions", (CashService s, string? accountId, int? days, int? limit) =>
            s.Transactions(accountId, days ?? 30, limit ?? 100));
    }
}

public static class ForecastEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/forecast");
        g.MapGet("", (ForecastService s, TimeProvider clock, string? scope, int? horizonDays, double? confidence) =>
        {
            var sc = s.ResolveScope(scope);
            int horizon = horizonDays ?? 91;
            double conf = confidence ?? 0.9;
            var r = s.Forecast(sc, horizon, conf);
            var series = s.Series(sc);
            var history = Enumerable.Range(series.Length - 60, 60)
                .Select(i => new HistoryPointDto(s.HistoryStart.AddDays(i), Math.Round(series[i], 2))).ToList();
            var daily = Enumerable.Range(0, r.Point.Length).Select(i => new ForecastPointDto(r.Dates[i],
                Math.Round(r.Point[i], 2), Math.Round(r.Lower[i], 2), Math.Round(r.Upper[i], 2),
                Math.Round(r.CumPoint[i], 2), Math.Round(r.CumLower[i], 2), Math.Round(r.CumUpper[i], 2))).ToList();
            return new ForecastDto(sc.Key, sc.OutputCurrency, horizon, conf, clock.GetUtcNow(),
                new ForecastModelDto("cash-forecast-ensemble", Math.Round(r.WeightHoltWinters, 4), Math.Round(r.WeightRidge, 4), r.CalibrationOrigins),
                daily, history);
        });
        g.MapGet("/backtest", (ForecastService s, string? scope, int? horizonDays) =>
            s.Backtest(s.ResolveScope(scope), horizonDays ?? 28));
    }
}

public static class FxEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/fx");
        g.MapGet("/rates", (FxService s) => s.Rates());
        g.MapGet("/exposures", (FxService s) => s.Exposures());
        g.MapGet("/recommendations", (FxService s) => s.Recommendations());
        g.MapGet("/regimes", (FxService s) => s.Regimes());
        g.MapGet("/hedges", (FxService s) => s.Hedges());
        g.MapPost("/hedges", (CreateHedgeRequest r, HttpContext c, FxService s) =>
            Results.Created("/api/v1/fx/hedges", s.BookHedge(r, c.User.ToActor()))).RequireAuthorization(Policies.Maker);
        g.MapPost("/hedges/{id}/cancel", (string id, HttpContext c, FxService s) =>
            s.CancelHedge(id, c.User.ToActor())).RequireAuthorization(Policies.Maker);
    }
}

public static class RiskEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/risk");
        g.MapGet("/metrics", (RiskService s) => s.Metrics());
        g.MapGet("/stress-scenarios", (RiskService s) => s.Stress());
        g.MapGet("/limits", (RiskService s) => s.Limits());
    }
}

public static class InHouseBankEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/inhouse-bank");
        g.MapGet("/balances", (NettingService s) => s.Balances());
        g.MapGet("/netting/preview", (NettingService s, string? settlementCurrency) => s.Preview(settlementCurrency));
        g.MapPost("/netting/run", (RunNettingRequest? r, HttpContext c, NettingService s) =>
            Results.Created("/api/v1/inhouse-bank/netting/cycles", s.Run(r?.SettlementCurrency, c.User.ToActor()))).RequireAuthorization(Policies.Maker);
        g.MapGet("/netting/cycles", (NettingService s) => s.Cycles());
    }
}

public static class DebtEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/debt");
        g.MapGet("/summary", (DebtService s) => s.Summary());
        g.MapGet("/facilities", (DebtService s) => s.Summary().Facilities);
        g.MapGet("/covenants", (DebtService s) => s.Covenants());
    }
}

public static class PaymentEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/payments");
        g.MapGet("", (PaymentService s, string? status) => s.List(status));
        g.MapGet("/{id}", (string id, PaymentService s) => s.Get(id));
        g.MapPost("", (CreatePaymentRequest r, HttpContext c, PaymentService s) =>
        {
            var p = s.Create(r, c.User.ToActor());
            return Results.Created($"/api/v1/payments/{p.Id}", p);
        }).RequireAuthorization(Policies.Maker);
        g.MapPost("/{id}/submit", (string id, HttpContext c, PaymentService s) => s.Submit(id, c.User.ToActor()));
        g.MapPost("/{id}/approve", (string id, HttpContext c, PaymentService s) => s.Approve(id, c.User.ToActor()))
            .RequireAuthorization(Policies.Checker);
        g.MapPost("/{id}/reject", (string id, RejectPaymentRequest r, HttpContext c, PaymentService s) =>
            s.Reject(id, r.Reason, c.User.ToActor())).RequireAuthorization(Policies.Checker);
        g.MapPost("/{id}/release", (string id, HttpContext c, PaymentService s) => s.Release(id, c.User.ToActor()))
            .RequireAuthorization(Policies.Maker);
    }
}

public static class AnomalyEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/anomalies");
        g.MapGet("", (BallastDb db, ModelHub models, IRateProvider rates, BallastSettings settings, int? limit) =>
        {
            var from = settings.Today.AddDays(-30);
            var accounts = db.Accounts.All().ToDictionary(a => a.Id);
            var items = new List<AnomalyItemDto>();
            if (!models.DetectorAvailable) return items;
            foreach (var t in db.Transactions.Where(t => t.Amount < 0 && t.Date >= from && accounts.ContainsKey(t.AccountId)))
            {
                var a = models.Assess(t.AccountId, t.Counterparty, Math.Abs(t.Amount), t.Currency, t.Date);
                if (!a.Flagged) continue;
                items.Add(new AnomalyItemDto("transaction", t.Id, t.AccountId, t.Counterparty,
                    Math.Round(Math.Abs(FxMath.Convert(rates, t.Amount, t.Currency, settings.BaseCurrency)), 2), t.Date,
                    Math.Round(a.Score, 4), a.Reasons));
            }
            foreach (var p in db.Payments.All().Where(p => p.RiskFlags.Count > 0 && p.Status != PaymentStatus.Rejected))
                items.Add(new AnomalyItemDto("payment", p.Id, p.AccountId, p.Beneficiary,
                    Math.Round(FxMath.Convert(rates, p.Amount, p.Currency, settings.BaseCurrency), 2), p.ValueDate, 1.0, p.RiskFlags));
            return items.OrderByDescending(i => i.Score).Take(Math.Clamp(limit ?? 20, 1, 100)).ToList();
        });
        g.MapPost("/assess", (AssessPaymentRequest r, PaymentService s) => s.Assess(r));
    }
}

public static class AuditEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/audit");
        g.MapGet("", (AuditLog log, int? limit) => log.Recent(Math.Clamp(limit ?? 50, 1, 500)));
        g.MapGet("/verify", (AuditLog log) => log.Verify());
    }
}

public static class AiEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/ai");
        g.MapGet("/models", (ModelHub models, FxService fx, ForecastService forecasts) =>
        {
            var backtest = forecasts.Backtest(forecasts.ResolveScope("all"), 28);
            return new AiOverviewDto(ModelCatalog.Models, Math.Round(models.Detector.Threshold, 4), models.Agent.Episodes,
                fx.Regimes(), backtest);
        });
    }
}

public static class MetaEndpoints
{
    public static void Map(WebApplication app, BallastSettings settings)
    {
        app.MapGet("/", () => new { name = "Ballast", description = "Treasury and liquidity management API", version = "1.0.0", docs = "/api/v1/meta/endpoints" });
        app.MapGet("/health", () => new { status = "ok", environment = settings.Environment, asOf = settings.Today, baseCurrency = settings.BaseCurrency });
        app.MapGet("/api/v1/meta/endpoints", (EndpointDataSource source) =>
            source.Endpoints.OfType<RouteEndpoint>()
                .Select(e => new
                {
                    route = e.RoutePattern.RawText,
                    methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.ToArray() ?? []
                })
                .Where(e => e.route is not null && e.route.StartsWith("/api/", StringComparison.Ordinal))
                .OrderBy(e => e.route, StringComparer.Ordinal).ToList()).RequireAuthorization();
    }
}
