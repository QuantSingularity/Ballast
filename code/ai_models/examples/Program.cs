using Ballast.AiModels.Agents;
using Ballast.AiModels.Anomaly;
using Ballast.AiModels.Config;
using Ballast.AiModels.Environments;
using Ballast.AiModels.Forecasting;
using Ballast.AiModels.Generative;
using Ballast.AiModels.Research;

var command = args.FirstOrDefault() ?? "all";
var examples = new Dictionary<string, Action>
{
    ["forecast"] = ForecastExample,
    ["anomaly"] = AnomalyExample,
    ["regimes"] = RegimeExample,
    ["hedging"] = HedgingExample
};

if (command == "all") foreach (var e in examples.Values) { e(); Console.WriteLine(); }
else if (examples.TryGetValue(command, out var run)) run();
else
{
    Console.WriteLine("Usage: dotnet run -- [forecast|anomaly|regimes|hedging|all]");
    return 1;
}
return 0;

static void ForecastExample()
{
    Console.WriteLine("Cash flow forecast with calibrated intervals");
    var start = new DateOnly(2025, 1, 1);
    var y = SyntheticCashFlowGenerator.DailyNet(SyntheticCashFlowGenerator.Generate(start, 540, new FlowProfile(1.0, -0.10, 5)), start, 540);
    var f = EnsembleCashForecaster.Forecast(y, start, 28, 0.9, AiModelsConfig.DefaultCalibrationOrigins);
    Console.WriteLine($"  weights: Holt-Winters {f.WeightHoltWinters * 100:F0}%, ridge {f.WeightRidge * 100:F0}%, calibration origins {f.CalibrationOrigins}");
    for (int h = 0; h < 7; h++)
        Console.WriteLine($"  {f.Dates[h]:yyyy-MM-dd}  point {f.Point[h],12:N0}  90% [{f.Lower[h],12:N0}, {f.Upper[h],12:N0}]");
    var m = ForecastBacktest.Run(y, start, 28, 6, 12);
    Console.WriteLine($"  backtest: MASE {m.Mase:F2} vs seasonal naive, interval coverage {m.IntervalCoverage * 100:F0}% (nominal {m.Confidence * 100:F0}%)");
}

static void AnomalyExample()
{
    Console.WriteLine("Payment anomaly screening");
    var start = new DateOnly(2025, 1, 1);
    var history = SyntheticCashFlowGenerator.Generate(start, 400, new FlowProfile(1.0, 0.0, 9))
        .Where(f => f.Amount < 0).Select(f => new OutflowRecord("ACC-1", f.Counterparty, f.Amount, f.Date)).ToList();
    var detector = new PaymentAnomalyDetector();
    detector.Fit(history);
    foreach (var (who, amount) in new[] { ("Globex Materials", 21_000.0), ("Globex Materials", 400_000.0), ("Unknown Holdings Ltd", 900_000.0) })
    {
        var a = detector.Assess("ACC-1", who, amount, new DateOnly(2026, 3, 10));
        Console.WriteLine($"  {who,-22} {amount,10:N0}  score {a.Score:F2}  flagged {a.Flagged}");
        foreach (var r in a.Reasons) Console.WriteLine($"      - {r}");
    }
}

static void RegimeExample()
{
    Console.WriteLine("Hidden Markov regime detection on FX returns");
    var truth = new RegimeSwitchingSimulator([0.0, 0.0], [0.005, 0.02], new[,] { { 0.97, 0.03 }, { 0.08, 0.92 } }, 4);
    var hmm = GaussianHmm.Fit(truth.SimulateReturns(1, 2500)[0]);
    Console.WriteLine($"  fitted sigma: calm {hmm.Sigmas[0]:F4}, stress {hmm.Sigmas[1]:F4} (truth 0.0050, 0.0200)");
    Console.WriteLine($"  persistence: calm {hmm.Transition[0, 0]:F2}, stress {hmm.Transition[1, 1]:F2} (truth 0.97, 0.92)");
}

static void HedgingExample()
{
    Console.WriteLine("Reinforcement learning hedge ratio policy");
    var env = new FxHedgingEnvironment(new FxHedgingConfig(), 1);
    var agent = new QLearningHedger();
    agent.Train(env, 25_000, 17);
    var levels = AiModelsConfig.HedgeRatioLevels;
    for (int regime = 0; regime < 2; regime++)
    {
        var picks = Enumerable.Range(0, 3).Select(b => levels[agent.Greedy(FxHedgingEnvironment.EncodeState(b, 0, regime))]);
        Console.WriteLine($"  {(regime == 0 ? "calm  " : "stress")} regime, hedge ratio by decision month: {string.Join(", ", picks.Select(p => $"{p * 100:F0}%"))}");
    }
    var eval = new FxHedgingEnvironment(new FxHedgingConfig(), 99);
    var learned = QLearningHedger.Evaluate(eval, agent.Greedy, 600, 5000);
    var none = QLearningHedger.Evaluate(eval, _ => 0, 600, 5000);
    Console.WriteLine($"  utility: learned {learned.MeanUtility:F4} vs unhedged {none.MeanUtility:F4}; P&L std {learned.PnlStdDev:F3} vs {none.PnlStdDev:F3}");
}
