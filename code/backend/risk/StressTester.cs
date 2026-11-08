namespace Ballast.Risk;

public sealed record LiquiditySnapshot(
    decimal TotalCashBase,
    decimal NonBaseCashBase,
    decimal LargestBankBase,
    decimal UndrawnCommittedBase,
    decimal Receipts30dBase,
    decimal Outflows30dBase,
    decimal FloatingDebtBase);

public sealed record StressResult(
    string Id, string Name, string Description, decimal LiquidityImpactBase, decimal LiquidityAfterBase, decimal CoverageRatioAfter);

public static class StressTester
{
    public static IReadOnlyList<StressResult> Run(LiquiditySnapshot s)
    {
        decimal baseLiquidity = s.TotalCashBase + s.UndrawnCommittedBase;
        decimal fx = -0.10m * s.NonBaseCashBase;
        decimal bank = -s.LargestBankBase;
        decimal delay = -0.30m * s.Receipts30dBase;
        decimal rates = -0.02m * s.FloatingDebtBase * 90m / 365m;
        var scenarios = new (string Id, string Name, string Description, decimal Impact)[]
        {
            ("fx-shock", "FX shock", "All non-base currencies depreciate 10% against the base currency", fx),
            ("bank-failure", "Counterparty bank failure", "The largest banking counterparty freezes all balances", bank),
            ("collections-delay", "Collections delay", "30% of the next 30 days of customer receipts slip beyond the horizon", delay),
            ("rates-shock", "Rates +200bp", "Floating-rate debt reprices 200bp higher for one quarter", rates),
            ("combined-severe", "Combined severe", "FX shock, collections delay and the loss of the largest bank together", fx + bank + delay)
        };
        return scenarios.Select(x =>
        {
            decimal after = baseLiquidity + x.Impact;
            decimal coverage = s.Outflows30dBase <= 0 ? 99m : after / s.Outflows30dBase;
            return new StressResult(x.Id, x.Name, x.Description, x.Impact, after, coverage);
        }).ToList();
    }
}
