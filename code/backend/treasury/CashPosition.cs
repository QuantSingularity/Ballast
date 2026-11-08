using Ballast.Core.Domain;
using Ballast.MarketData;

namespace Ballast.Treasury;

public sealed record CurrencyPosition(string Currency, decimal Native, decimal Base, decimal Share);

public sealed record EntityPosition(string EntityId, string Name, decimal Base);

public sealed record BankPosition(string Bank, decimal Base, decimal Share);

public sealed record AccountPosition(BankAccount Account, decimal BalanceBase);

public sealed record CashPosition(
    string BaseCurrency,
    decimal TotalBase,
    IReadOnlyList<CurrencyPosition> ByCurrency,
    IReadOnlyList<EntityPosition> ByEntity,
    IReadOnlyList<BankPosition> ByBank,
    IReadOnlyList<AccountPosition> Accounts);

public static class CashPositionCalculator
{
    public static CashPosition Compute(
        IReadOnlyList<LegalEntity> entities, IReadOnlyList<BankAccount> accounts, IRateProvider rates, string baseCurrency)
    {
        var rows = accounts.Select(a => new AccountPosition(a, Math.Round(FxMath.Convert(rates, a.Balance, a.Currency, baseCurrency), 2))).ToList();
        decimal total = rows.Sum(r => r.BalanceBase);
        decimal Share(decimal v) => total == 0 ? 0 : Math.Round(v / total, 4);

        var byCurrency = rows.GroupBy(r => r.Account.Currency)
            .Select(g => new CurrencyPosition(g.Key, g.Sum(x => x.Account.Balance), g.Sum(x => x.BalanceBase), Share(g.Sum(x => x.BalanceBase))))
            .OrderByDescending(c => c.Base).ToList();
        var names = entities.ToDictionary(e => e.Id, e => e.Name);
        var byEntity = rows.GroupBy(r => r.Account.EntityId)
            .Select(g => new EntityPosition(g.Key, names.GetValueOrDefault(g.Key, g.Key), g.Sum(x => x.BalanceBase)))
            .OrderByDescending(e => e.Base).ToList();
        var byBank = rows.GroupBy(r => r.Account.Bank)
            .Select(g => new BankPosition(g.Key, g.Sum(x => x.BalanceBase), Share(g.Sum(x => x.BalanceBase))))
            .OrderByDescending(b => b.Base).ToList();
        return new CashPosition(baseCurrency, total, byCurrency, byEntity, byBank, rows);
    }
}
