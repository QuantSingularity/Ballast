using Ballast.AiModels.Anomaly;
using Ballast.AiModels.Research;
using Ballast.TestKit;

namespace Ballast.AiModels.Tests;

public class AnomalyTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);

    private static PaymentAnomalyDetector Fit()
    {
        var flows = SyntheticCashFlowGenerator.Generate(Start, 400, new FlowProfile(1.0, 0.0, 9));
        var outflows = flows.Where(f => f.Amount < 0)
            .Select(f => new OutflowRecord("ACC-1", f.Counterparty, f.Amount, f.Date)).ToList();
        var d = new PaymentAnomalyDetector();
        d.Fit(outflows);
        return d;
    }

    [Fact]
    public void RoutinePayment_IsNotFlagged()
    {
        var d = Fit();
        var a = d.Assess("ACC-1", "Globex Materials", 21_000, new DateOnly(2026, 3, 10));
        Assert.False(a.Flagged, string.Join("; ", a.Reasons));
    }

    [Fact]
    public void LargePaymentToNewBeneficiary_IsFlagged()
    {
        var d = Fit();
        var a = d.Assess("ACC-1", "Unknown Holdings Ltd", 1_400_000, new DateOnly(2026, 3, 10));
        Assert.True(a.Flagged);
        Assert.True(a.NewCounterparty);
        Assert.True(a.Reasons.Count > 0);
    }

    [Fact]
    public void ExtremeAmount_ScoresHigherThanTypical()
    {
        var d = Fit();
        var typical = d.Assess("ACC-1", "Globex Materials", 20_000, new DateOnly(2026, 3, 10));
        var extreme = d.Assess("ACC-1", "Globex Materials", 5_000_000, new DateOnly(2026, 3, 10));
        Assert.True(extreme.Score > typical.Score);
    }

    [Fact]
    public void UnfittedDetector_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PaymentAnomalyDetector().Assess("A", "B", 1, new DateOnly(2026, 1, 5)));
    }
}
