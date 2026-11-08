using Ballast.Api.Schemas;
using Ballast.Core;
using Ballast.Core.Domain;
using Ballast.Data;
using Ballast.MarketData;
using Ballast.Risk;

namespace Ballast.Api.Services;

public sealed class RiskService(
    BallastDb db, IRateProvider rates, BallastSettings settings, ForecastService forecasts,
    CashService cash, DebtService debt, FxService fx, ModelHub models)
{
    private LiquiditySnapshot Snapshot()
    {
        string b = settings.BaseCurrency;
        var pos = cash.Position();
        decimal nonBase = pos.Accounts.Where(a => a.Account.Currency != b).Sum(a => a.BalanceBase);
        decimal largestBank = pos.ByBank.Count == 0 ? 0 : pos.ByBank.Max(x => x.Base);
        var from = settings.Today.AddDays(-30);
        var recent = db.Transactions.Where(t => t.Date >= from).ToList();
        decimal receipts = recent.Where(t => t.Category == "Receipts").Sum(t => FxMath.Convert(rates, t.Amount, t.Currency, b));
        decimal outflows = recent.Where(t => t.Amount < 0).Sum(t => -FxMath.Convert(rates, t.Amount, t.Currency, b));
        return new LiquiditySnapshot(pos.TotalBase, nonBase, largestBank, debt.UndrawnBase(), receipts, outflows, debt.FloatingDebtBase());
    }

    public IReadOnlyList<StressResult> Stress() => StressTester.Run(Snapshot());

    public IReadOnlyList<LimitResult> Limits()
    {
        var snap = Snapshot();
        return LimitMonitor.Evaluate(settings, cash.Position().ByBank, snap.TotalCashBase + snap.UndrawnCommittedBase,
            fx.Exposures().Lines, debt.Covenants());
    }

    public RiskMetricsDto Metrics()
    {
        var snap = Snapshot();
        var forecast = forecasts.Forecast(forecasts.ResolveScope("all"), 91, 0.9);
        var car = new List<CashAtRiskResult>();
        foreach (int horizon in new[] { 30, 91 })
            foreach (double level in new[] { 0.95, 0.99 })
                car.Add(CashAtRiskCalculator.Compute(forecast.CumErrorSamples, horizon, level));

        var exposures = fx.Exposures().Lines;
        var fxVar = new List<FxVarDto>();
        foreach (var ccy in forecasts.ForeignCurrencies())
        {
            decimal exposureBase = Math.Abs(exposures.Where(l => l.Currency == ccy && l.BucketIndex == 0).Sum(l => l.NetBase));
            var r = FxAtRisk.Compute(models.Hmm(ccy), rates.LogReturns(ccy, 60), (double)exposureBase, 21, 0.95, 31);
            fxVar.Add(new FxVarDto(ccy, Currencies.Round(exposureBase, settings.BaseCurrency), Math.Round(r.Var, 2),
                Math.Round(r.ExpectedShortfall, 2), Math.Round(r.HighVolProbability, 4), r.Sigmas));
        }
        decimal liquidity = snap.TotalCashBase + snap.UndrawnCommittedBase;
        decimal lcr = snap.Outflows30dBase <= 0 ? 99m : Math.Round(liquidity / snap.Outflows30dBase, 2);
        return new RiskMetricsDto(settings.Today, settings.BaseCurrency, car, Currencies.Round(liquidity, settings.BaseCurrency), lcr,
            fxVar, Math.Round(fxVar.Sum(v => v.Var95), 2));
    }
}
