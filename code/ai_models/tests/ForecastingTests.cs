using Ballast.AiModels.Forecasting;
using Ballast.AiModels.Numerics;
using Ballast.AiModels.Research;
using Ballast.TestKit;

namespace Ballast.AiModels.Tests;

public class ForecastingTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);

    private static double[] Series(int days = 540, int seed = 5) =>
        SyntheticCashFlowGenerator.DailyNet(
            SyntheticCashFlowGenerator.Generate(Start, days, new FlowProfile(1.0, 0.0, seed)), Start, days);

    [Fact]
    public void HoltWinters_RecoversWeeklySeasonality()
    {
        var y = Enumerable.Range(0, 140).Select(t => 100 + 20 * Math.Sin(2 * Math.PI * (t % 7) / 7.0)).ToArray();
        var hw = HoltWintersAdditive.Fit(y);
        var f = hw.Forecast(14);
        for (int h = 0; h < 14; h++)
            Assert.Equal(100 + 20 * Math.Sin(2 * Math.PI * ((140 + h) % 7) / 7.0), f[h], 1.0);
    }

    [Fact]
    public void Ridge_RecoversLinearRelationship()
    {
        var rng = new Rng(3);
        var x = Enumerable.Range(0, 400).Select(_ => new[] { rng.NextNormal(), rng.NextNormal() }).ToList();
        var y = x.Select(r => 3 * r[0] - 2 * r[1] + 5).ToList();
        var model = new RidgeRegressor(0.01);
        model.Fit(x, y);
        Assert.Equal(3 * 1.0 - 2 * 2.0 + 5, model.Predict([1.0, 2.0]), 0.05);
    }

    [Fact]
    public void Ensemble_WeightsSumToOneAndIntervalsBracketPoint()
    {
        var y = Series();
        var r = EnsembleCashForecaster.Forecast(y, Start, 91, 0.9, 12);
        Assert.Equal(1.0, r.WeightHoltWinters + r.WeightRidge, 1e-9);
        Assert.Equal(91, r.Point.Length);
        for (int h = 0; h < 91; h++)
        {
            Assert.True(r.Lower[h] <= r.Point[h] && r.Point[h] <= r.Upper[h]);
            Assert.True(r.CumLower[h] <= r.CumUpper[h]);
        }
        Assert.True(r.CalibrationOrigins >= 5);
    }

    [Fact]
    public void Ensemble_BeatsSeasonalNaiveOnSyntheticFlows()
    {
        var y = Series();
        var m = ForecastBacktest.Run(y, Start, 28, 6, 8, 0.9);
        Assert.True(m.Origins >= 4);
        Assert.True(m.Mase < 1.0, $"MASE was {m.Mase:F3}");
    }

    [Fact]
    public void CalibratedIntervals_CoverMostActuals()
    {
        var y = Series();
        var m = ForecastBacktest.Run(y, Start, 28, 6, 12, 0.9);
        Assert.True(m.IntervalCoverage >= 0.70, $"Coverage was {m.IntervalCoverage:P0}");
    }

    [Fact]
    public void Forecast_RejectsShortHistory()
    {
        Assert.Throws<ArgumentException>(() => EnsembleCashForecaster.Fit(new double[30], Start));
    }
}
