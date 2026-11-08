using Ballast.AiModels.Agents;
using Ballast.AiModels.Environments;
using Ballast.AiModels.Generative;
using Ballast.AiModels.Numerics;
using Ballast.AiModels.Research;
using Ballast.TestKit;

namespace Ballast.AiModels.Tests;

public class ModelsTests
{
    [Fact]
    public void Hmm_RecoversRegimeVolatilities()
    {
        var sim = new RegimeSwitchingSimulator([0.0, 0.0], [0.005, 0.02], new[,] { { 0.97, 0.03 }, { 0.08, 0.92 } }, 4);
        var returns = sim.SimulateReturns(1, 2500)[0];
        var hmm = GaussianHmm.Fit(returns);
        Assert.Equal(0.005, hmm.Sigmas[0], 0.002);
        Assert.Equal(0.02, hmm.Sigmas[1], 0.005);
        Assert.True(hmm.Sigmas[0] < hmm.Sigmas[1]);
    }

    [Fact]
    public void Hmm_FilterIdentifiesHighVolatilityTail()
    {
        var calm = new RegimeSwitchingSimulator([0.0, 0.0], [0.005, 0.02], new[,] { { 1.0, 0.0 }, { 0.0, 1.0 } }, 8);
        var hmm = GaussianHmm.Fit(new RegimeSwitchingSimulator([0.0, 0.0], [0.005, 0.02], new[,] { { 0.97, 0.03 }, { 0.08, 0.92 } }, 4).SimulateReturns(1, 2500)[0]);
        var stressed = calm.SimulateReturns(1, 25, startRegime: 1)[0];
        var quiet = calm.SimulateReturns(1, 25, startRegime: 0)[0];
        double pStressed = hmm.FilterLast(stressed)[1];
        double pQuiet = hmm.FilterLast(quiet)[1];
        Assert.True(pStressed > 0.5, $"stressed p(high) was {pStressed:F2}");
        Assert.True(pQuiet < 0.2, $"quiet p(high) was {pQuiet:F2}");
        Assert.True(pStressed > pQuiet + 0.4);
    }

    [Fact]
    public void QLearning_OutperformsNoHedgeAndRandomPolicies()
    {
        var env = new FxHedgingEnvironment(new FxHedgingConfig(), 1);
        var agent = new QLearningHedger();
        agent.Train(env, 25_000, 17);
        var evalEnv = new FxHedgingEnvironment(new FxHedgingConfig(), 99);
        var learned = QLearningHedger.Evaluate(evalEnv, agent.Greedy, 600, 5000);
        var none = QLearningHedger.Evaluate(evalEnv, _ => 0, 600, 5000);
        var rng = new Rng(2);
        var random = QLearningHedger.Evaluate(evalEnv, _ => rng.NextInt(FxHedgingEnvironment.ActionCount), 600, 5000);
        Assert.True(learned.MeanUtility > none.MeanUtility, $"learned {learned.MeanUtility:F4} vs none {none.MeanUtility:F4}");
        Assert.True(learned.MeanUtility > random.MeanUtility, $"learned {learned.MeanUtility:F4} vs random {random.MeanUtility:F4}");
        Assert.True(learned.PnlStdDev < none.PnlStdDev);
    }

    [Fact]
    public void QLearning_HedgesMoreInStressThanInCalm()
    {
        var agent = new QLearningHedger();
        agent.Train(new FxHedgingEnvironment(new FxHedgingConfig(), 17), 25_000, 17);
        var levels = Config.AiModelsConfig.HedgeRatioLevels;
        double calm = 0, stress = 0;
        for (int bucket = 0; bucket < 3; bucket++)
        {
            calm += levels[agent.Greedy(FxHedgingEnvironment.EncodeState(bucket, 0, 0))];
            stress += levels[agent.Greedy(FxHedgingEnvironment.EncodeState(bucket, 0, 1))];
        }
        Assert.True(stress > calm, $"stress {stress:F2} vs calm {calm:F2}");
    }

    [Fact]
    public void RiskMetrics_MatchNormalTheory()
    {
        var rng = new Rng(21);
        var pnl = Enumerable.Range(0, 50_000).Select(_ => 100 * rng.NextNormal()).ToArray();
        Assert.Equal(164.5, RiskMetrics.HistoricalVar(pnl, 0.95), 6.0);
        Assert.Equal(164.5, RiskMetrics.ParametricVar(pnl, 0.95), 4.0);
        Assert.True(RiskMetrics.ExpectedShortfall(pnl, 0.95) > RiskMetrics.HistoricalVar(pnl, 0.95));
    }

    [Fact]
    public void Stats_QuantileAndInverseNormal()
    {
        Assert.Equal(2.5, Stats.Median([1.0, 2.0, 3.0, 4.0]), 1e-12);
        Assert.Equal(1.959964, Stats.NormalInvCdf(0.975), 1e-4);
        Assert.Equal(0.0, Stats.NormalInvCdf(0.5), 1e-9);
    }

    [Fact]
    public void IsolationForest_ScoresOutlierHigher()
    {
        var rng = new Rng(1);
        var data = Enumerable.Range(0, 500).Select(_ => new[] { rng.NextNormal(), rng.NextNormal() }).ToList();
        var f = new Anomaly.IsolationForest();
        f.Fit(data);
        Assert.True(f.Score([8.0, 8.0]) > f.Score([0.0, 0.0]) + 0.1);
    }
}
