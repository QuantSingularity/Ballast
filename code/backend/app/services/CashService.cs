using Ballast.Api.Schemas;
using Ballast.Core;
using Ballast.Core.Domain;
using Ballast.Data;
using Ballast.MarketData;
using Ballast.Treasury;

namespace Ballast.Api.Services;

public sealed class CashService(BallastDb db, IRateProvider rates, BallastSettings settings, ForecastService forecasts)
{
    public CashPosition Position() =>
        CashPositionCalculator.Compute(db.Entities.All(), db.Accounts.All(), rates, settings.BaseCurrency);

    public CashSummaryDto Summary()
    {
        var p = Position();
        return new CashSummaryDto(settings.Today, p.BaseCurrency, Currencies.Round(p.TotalBase, p.BaseCurrency), p.Accounts.Count,
            p.ByCurrency, p.ByEntity, p.ByBank);
    }

    public IReadOnlyList<AccountDto> Accounts()
    {
        var names = db.Entities.All().ToDictionary(e => e.Id, e => e.Name);
        return Position().Accounts.Select(a => new AccountDto(a.Account.Id, a.Account.EntityId,
            names.GetValueOrDefault(a.Account.EntityId, a.Account.EntityId), a.Account.Bank, a.Account.Iban, a.Account.Currency,
            a.Account.Balance, Currencies.Round(a.BalanceBase, settings.BaseCurrency), a.Account.Kind)).ToList();
    }

    public IReadOnlyList<CashTransaction> Transactions(string? accountId, int days, int limit)
    {
        days = Math.Clamp(days, 1, 540);
        limit = Math.Clamp(limit, 1, 500);
        var from = settings.Today.AddDays(-days);
        return db.Transactions.Where(t => t.Date >= from && (accountId is null || t.AccountId == accountId))
            .OrderByDescending(t => t.Date).ThenBy(t => t.Id, StringComparer.Ordinal).Take(limit).ToList();
    }

    public LadderDto Ladder()
    {
        var total = Position().TotalBase;
        var forecast = forecasts.Forecast(forecasts.ResolveScope("all"), 91, 0.9);
        var scheduled = db.Payments.All().Where(p => p.Status is PaymentStatus.Approved or PaymentStatus.PendingApproval).ToList();
        var buckets = new List<LadderBucketDto>();
        foreach (var (label, days) in new[] { ("7 days", 7), ("30 days", 30), ("91 days", 91) })
        {
            int idx = days - 1;
            decimal sched = scheduled.Where(p => p.ValueDate > settings.Today && p.ValueDate <= settings.Today.AddDays(days))
                .Sum(p => FxMath.Convert(rates, p.Amount, p.Currency, settings.BaseCurrency));
            buckets.Add(new LadderBucketDto(label, days,
                Math.Round((double)total + forecast.CumPoint[idx], 2),
                Math.Round((double)total + forecast.CumLower[idx], 2),
                Math.Round((double)total + forecast.CumUpper[idx], 2),
                Currencies.Round(sched, settings.BaseCurrency)));
        }
        return new LadderDto(settings.Today, settings.BaseCurrency, Currencies.Round(total, settings.BaseCurrency), buckets);
    }
}
