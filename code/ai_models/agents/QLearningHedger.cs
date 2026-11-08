using Ballast.AiModels.Environments;
using Ballast.AiModels.Numerics;

namespace Ballast.AiModels.Agents;

public sealed record PolicyEvaluation(double MeanUtility, double MeanPnl, double PnlStdDev);

public sealed class QLearningHedger(double alpha = 0.1, double gamma = 0.99)
{
    private readonly double[,] _q = new double[FxHedgingEnvironment.StateCount, FxHedgingEnvironment.ActionCount];

    public int Episodes { get; private set; }

    public void Train(FxHedgingEnvironment env, int episodes, int seed)
    {
        var rng = new Rng(seed);
        for (int ep = 0; ep < episodes; ep++)
        {
            double eps = Math.Max(0.05, 1.0 - (double)ep / (episodes * 0.8));
            int state = env.Reset(seed + ep, rng.NextInt(2), rng.NextInt(FxHedgingEnvironment.ActionCount));
            bool done = false;
            while (!done)
            {
                int action = rng.NextDouble() < eps ? rng.NextInt(FxHedgingEnvironment.ActionCount) : Greedy(state);
                var (next, reward, _, d) = env.Step(action);
                double best = double.MinValue;
                for (int a = 0; a < FxHedgingEnvironment.ActionCount; a++) best = Math.Max(best, _q[next, a]);
                double target = d ? reward : reward + gamma * best;
                _q[state, action] += alpha * (target - _q[state, action]);
                state = next;
                done = d;
            }
        }
        Episodes += episodes;
    }

    public double QValue(int state, int action) => _q[state, action];

    public int Greedy(int state)
    {
        int best = 0;
        for (int a = 1; a < FxHedgingEnvironment.ActionCount; a++)
            if (_q[state, a] > _q[state, best]) best = a;
        return best;
    }

    public static PolicyEvaluation Evaluate(FxHedgingEnvironment env, Func<int, int> policy, int episodes, int seed)
    {
        var utilities = new List<double>();
        var pnls = new List<double>();
        for (int ep = 0; ep < episodes; ep++)
        {
            int state = env.Reset(seed + ep, ep % 2);
            double u = 0, p = 0;
            bool done = false;
            while (!done)
            {
                var (next, reward, pnl, d) = env.Step(policy(state));
                u += reward;
                p += pnl;
                state = next;
                done = d;
            }
            utilities.Add(u);
            pnls.Add(p);
        }
        return new PolicyEvaluation(Stats.Mean(utilities), Stats.Mean(pnls), Stats.StdDev(pnls));
    }
}
