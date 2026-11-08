using Ballast.Api.Schemas;
using Ballast.Core;
using Ballast.Core.Domain;
using Ballast.Data;
using Ballast.MarketData;
using Ballast.Treasury;

namespace Ballast.Api.Services;

public sealed class DebtService(BallastDb db, IRateProvider rates, BallastSettings settings, CashService cash)
{
    private static readonly HashSet<string> EbitdaCategories = ["Receipts", "Suppliers", "Payroll", "Rent", "Fees"];

    public CovenantMetrics Metrics()
    {
        string b = settings.BaseCurrency;
        decimal total = cash.Position().TotalBase;
        var facilities = db.Facilities.All();
        decimal drawn = facilities.Sum(f => FxMath.Convert(rates, f.Drawn, f.Currency, b));
        decimal undrawn = facilities.Sum(f => FxMath.Convert(rates, f.Commitment - f.Drawn, f.Currency, b));
        decimal interest = facilities.Sum(f => FxMath.Convert(rates, f.Drawn * f.AnnualRate, f.Currency, b));
        var from = settings.Today.AddDays(-365);
        decimal ebitda = db.Transactions.Where(t => t.Date >= from && EbitdaCategories.Contains(t.Category))
            .Sum(t => FxMath.Convert(rates, t.Amount, t.Currency, b));
        return new CovenantMetrics(total + undrawn, drawn - total, ebitda, interest);
    }

    public decimal UndrawnBase() =>
        db.Facilities.All().Sum(f => FxMath.Convert(rates, f.Commitment - f.Drawn, f.Currency, settings.BaseCurrency));

    public decimal FloatingDebtBase() =>
        db.Facilities.All().Where(f => f.Floating).Sum(f => FxMath.Convert(rates, f.Drawn, f.Currency, settings.BaseCurrency));

    public IReadOnlyList<CovenantResult> Covenants() => CovenantMonitor.Evaluate(db.Facilities.All(), Metrics());

    public DebtSummaryDto Summary()
    {
        string b = settings.BaseCurrency;
        var m = Metrics();
        var facilities = db.Facilities.All().Select(f => new FacilityDto(f.Id, f.EntityId, f.Name, f.Currency, f.Commitment, f.Drawn,
            f.Commitment - f.Drawn, f.Commitment == 0 ? 0 : Math.Round(f.Drawn / f.Commitment, 4), f.AnnualRate, f.Floating, f.Maturity,
            Currencies.Round(FxMath.Convert(rates, f.Drawn, f.Currency, b), b),
            Currencies.Round(FxMath.Convert(rates, f.Commitment - f.Drawn, f.Currency, b), b))).ToList();
        return new DebtSummaryDto(b, Currencies.Round(facilities.Sum(f => f.DrawnBase), b), Currencies.Round(m.NetDebtBase, b),
            Currencies.Round(m.LiquidityBase, b), Currencies.Round(m.EbitdaProxyBase, b), Currencies.Round(m.AnnualInterestBase, b),
            facilities, CovenantMonitor.Evaluate(db.Facilities.All(), m));
    }
}
