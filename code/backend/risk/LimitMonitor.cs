using Ballast.Core;
using Ballast.Treasury;

namespace Ballast.Risk;

public enum LimitStatus { Ok, Warning, Breach }

public sealed record LimitResult(string Id, string Name, decimal Actual, decimal Limit, string Unit, LimitStatus Status, string Detail);

public static class LimitMonitor
{
    public static IReadOnlyList<LimitResult> Evaluate(
        BallastSettings settings,
        IReadOnlyList<BankPosition> banks,
        decimal liquidityBase,
        IReadOnlyList<ExposureLine> exposures,
        IReadOnlyList<CovenantResult> covenants)
    {
        var results = new List<LimitResult>();

        foreach (var b in banks)
        {
            var status = b.Share > settings.MaxBankConcentration ? LimitStatus.Breach
                : b.Share > settings.MaxBankConcentration * 0.9m ? LimitStatus.Warning : LimitStatus.Ok;
            results.Add(new LimitResult($"conc-{b.Bank.ToLowerInvariant().Replace(' ', '-')}", $"Bank concentration: {b.Bank}",
                b.Share, settings.MaxBankConcentration, "share", status,
                $"{b.Share * 100:F1}% of group cash against a {settings.MaxBankConcentration * 100:F0}% limit"));
        }

        var liqStatus = liquidityBase < settings.MinLiquidityBuffer ? LimitStatus.Breach
            : liquidityBase < settings.MinLiquidityBuffer * 1.1m ? LimitStatus.Warning : LimitStatus.Ok;
        results.Add(new LimitResult("liquidity-buffer", "Minimum liquidity buffer", liquidityBase, settings.MinLiquidityBuffer,
            settings.BaseCurrency, liqStatus, "Cash plus undrawn committed facilities"));

        decimal[] floors = [0.50m, 0.30m, 0.10m];
        foreach (var grp in exposures.GroupBy(e => e.BucketIndex).OrderBy(g => g.Key))
        {
            decimal totalAbs = grp.Sum(e => Math.Abs(e.NetBase));
            if (totalAbs == 0) continue;
            decimal hedgedBase = grp.Sum(e => Math.Abs(e.NetBase) - Math.Abs(e.OpenBase));
            decimal ratio = hedgedBase / totalAbs;
            decimal floor = floors[Math.Min(grp.Key, floors.Length - 1)];
            var status = ratio < floor ? LimitStatus.Breach : ratio < floor + 0.05m ? LimitStatus.Warning : LimitStatus.Ok;
            string label = grp.First().Bucket;
            results.Add(new LimitResult($"hedge-ratio-{grp.Key}", $"Minimum hedge ratio: {label}", ratio, floor, "ratio", status,
                $"{ratio * 100:F0}% of forecast FX exposure hedged against a {floor * 100:F0}% policy floor"));
        }

        foreach (var c in covenants)
        {
            var status = c.Status switch
            {
                CovenantStatus.Breach => LimitStatus.Breach,
                CovenantStatus.Warning => LimitStatus.Warning,
                _ => LimitStatus.Ok
            };
            results.Add(new LimitResult($"cov-{c.CovenantId.ToLowerInvariant()}", $"Covenant: {c.Name} ({c.FacilityName})",
                c.Headroom, CovenantMonitor.WarningHeadroom, "headroom", status,
                $"{c.Headroom * 100:F0}% headroom, actual {c.Actual:F2} against {c.Threshold:F2}"));
        }
        return results;
    }
}
