using Ballast.Core.Domain;
using Ballast.MarketData;

namespace Ballast.Treasury;

public sealed record NettingPosition(string EntityId, decimal Net);

public sealed record NettingResult(
    string SettlementCurrency,
    decimal GrossVolume,
    decimal NetVolume,
    decimal ReductionPct,
    IReadOnlyList<NettingPosition> Positions,
    IReadOnlyList<NettingTransfer> Transfers);

public static class MultilateralNetting
{
    public static NettingResult Run(IReadOnlyList<IntercompanyBalance> balances, IRateProvider rates, string settlementCurrency)
    {
        var net = new Dictionary<string, decimal>();
        decimal gross = 0;
        foreach (var b in balances)
        {
            decimal amount = FxMath.Convert(rates, b.Amount, b.Currency, settlementCurrency);
            gross += amount;
            net[b.LenderEntityId] = net.GetValueOrDefault(b.LenderEntityId) + amount;
            net[b.BorrowerEntityId] = net.GetValueOrDefault(b.BorrowerEntityId) - amount;
        }

        var creditors = net.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, Remaining: kv.Value)).ToList();
        var debtors = net.Where(kv => kv.Value < 0).OrderBy(kv => kv.Value).Select(kv => (kv.Key, Remaining: -kv.Value)).ToList();
        var transfers = new List<NettingTransfer>();
        int ci = 0, di = 0;
        while (ci < creditors.Count && di < debtors.Count)
        {
            var c = creditors[ci];
            var d = debtors[di];
            decimal amt = Math.Min(c.Remaining, d.Remaining);
            if (amt > 0) transfers.Add(new NettingTransfer(d.Key, c.Key, amt));
            creditors[ci] = (c.Key, c.Remaining - amt);
            debtors[di] = (d.Key, d.Remaining - amt);
            if (creditors[ci].Remaining == 0) ci++;
            if (debtors[di].Remaining == 0) di++;
        }

        decimal netVolume = transfers.Sum(t => t.Amount);
        decimal reduction = gross == 0 ? 0 : 1 - netVolume / gross;
        var positions = net.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new NettingPosition(kv.Key, kv.Value)).ToList();
        return new NettingResult(settlementCurrency, gross, netVolume, reduction, positions, transfers);
    }
}
