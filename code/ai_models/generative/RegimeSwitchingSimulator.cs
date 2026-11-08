using Ballast.AiModels.Numerics;

namespace Ballast.AiModels.Generative;

public sealed class RegimeSwitchingSimulator(double[] means, double[] sigmas, double[,] transition, int seed)
{
    public static RegimeSwitchingSimulator FromHmm(GaussianHmm hmm, int seed) =>
        new(hmm.Means, hmm.Sigmas, hmm.Transition, seed);

    public double[][] SimulateReturns(int paths, int steps, int startRegime = 0)
    {
        var rng = new Rng(seed);
        var result = new double[paths][];
        for (int p = 0; p < paths; p++)
        {
            var r = new double[steps];
            int regime = startRegime;
            for (int t = 0; t < steps; t++)
            {
                r[t] = means[regime] + sigmas[regime] * rng.NextNormal();
                regime = rng.NextDouble() < transition[regime, 0] ? 0 : 1;
            }
            result[p] = r;
        }
        return result;
    }
}
