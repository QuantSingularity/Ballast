using Ballast.AiModels.Forecasting;

namespace Ballast.AiModels.Research;

public sealed record BacktestMetrics(
    int Origins,
    int Horizon,
    double MaeEnsemble,
    double MaeHoltWinters,
    double MaeRidge,
    double MaeSeasonalNaive,
    double Mase,
    double CumulativeMaeEnsemble,
    double CumulativeMaeSeasonalNaive,
    double IntervalCoverage,
    double Confidence);

public static class ForecastBacktest
{
    public static BacktestMetrics Run(double[] y, DateOnly start, int horizon, int outerOrigins, int innerOrigins, double confidence = 0.90)
    {
        double eEns = 0, eHw = 0, eRidge = 0, eNaive = 0, cEns = 0, cNaive = 0;
        long covered = 0, total = 0;
        int used = 0;
        for (int j = 0; j < outerOrigins; j++)
        {
            int o = y.Length - horizon - 14 * j;
            if (o < 200) break;
            var history = y[..o];
            var result = EnsembleCashForecaster.Forecast(history, start, horizon, confidence, innerOrigins);
            var parts = EnsembleCashForecaster.Fit(history, start);
            var hw = parts.PredictHoltWinters(horizon);
            var rd = parts.PredictRidge(horizon);
            double cumActual = 0, cumNaive = 0;
            for (int h = 0; h < horizon; h++)
            {
                double actual = y[o + h];
                double naive = y[o - 7 + h % 7];
                eEns += Math.Abs(actual - result.Point[h]);
                eHw += Math.Abs(actual - hw[h]);
                eRidge += Math.Abs(actual - rd[h]);
                eNaive += Math.Abs(actual - naive);
                cumActual += actual;
                cumNaive += naive;
                if (cumActual >= result.CumLower[h] && cumActual <= result.CumUpper[h]) covered++;
                total++;
            }
            cEns += Math.Abs(cumActual - result.CumPoint[horizon - 1]);
            cNaive += Math.Abs(cumActual - cumNaive);
            used++;
        }
        double n = Math.Max(1, used * horizon);
        return new BacktestMetrics(used, horizon, eEns / n, eHw / n, eRidge / n, eNaive / n,
            eNaive > 0 ? eEns / eNaive : 0, cEns / Math.Max(1, used), cNaive / Math.Max(1, used),
            total > 0 ? (double)covered / total : 0, confidence);
    }
}
