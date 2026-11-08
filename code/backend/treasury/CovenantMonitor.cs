using Ballast.Core.Domain;

namespace Ballast.Treasury;

public enum CovenantStatus { Pass, Warning, Breach }

public sealed record CovenantMetrics(decimal LiquidityBase, decimal NetDebtBase, decimal EbitdaProxyBase, decimal AnnualInterestBase);

public sealed record CovenantResult(
    string FacilityId, string FacilityName, string CovenantId, string Name, CovenantKind Kind,
    decimal Actual, decimal Threshold, decimal Headroom, CovenantStatus Status);

public static class CovenantMonitor
{
    public const decimal WarningHeadroom = 0.10m;

    public static IReadOnlyList<CovenantResult> Evaluate(IReadOnlyList<DebtFacility> facilities, CovenantMetrics m)
    {
        var results = new List<CovenantResult>();
        foreach (var f in facilities)
            foreach (var c in f.Covenants)
            {
                decimal actual, headroom;
                switch (c.Kind)
                {
                    case CovenantKind.MinLiquidity:
                        actual = m.LiquidityBase;
                        headroom = (actual - c.Threshold) / c.Threshold;
                        break;
                    case CovenantKind.MaxNetDebtToEbitda:
                        actual = m.EbitdaProxyBase <= 0 ? 99m : m.NetDebtBase / m.EbitdaProxyBase;
                        headroom = (c.Threshold - actual) / c.Threshold;
                        break;
                    default:
                        actual = m.AnnualInterestBase <= 0 ? 999m : m.EbitdaProxyBase / m.AnnualInterestBase;
                        headroom = (actual - c.Threshold) / c.Threshold;
                        break;
                }
                var status = headroom < 0 ? CovenantStatus.Breach : headroom < WarningHeadroom ? CovenantStatus.Warning : CovenantStatus.Pass;
                results.Add(new CovenantResult(f.Id, f.Name, c.Id, c.Name, c.Kind, Math.Round(actual, 4), c.Threshold, Math.Round(headroom, 4), status));
            }
        return results;
    }
}
