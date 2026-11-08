using Ballast.AiModels.Config;
using Ballast.AiModels.Numerics;

namespace Ballast.AiModels.Environments;

public sealed record FxHedgingConfig(
    int Steps = 4,
    double[]? Sigma = null,
    double[,]? Transition = null,
    double CostPerUnit = 0.004,
    double RiskAversion = 3.0)
{
    public double[] SigmaOrDefault => Sigma ?? [0.02, 0.05];
    public double[,] TransitionOrDefault => Transition ?? new[,] { { 0.95, 0.05 }, { 0.15, 0.85 } };
}

public sealed class FxHedgingEnvironment(FxHedgingConfig config, int seed)
{
    private Rng _rng = new(seed);
    private int _t;
    private int _hedgeIdx;
    private int _regime;

    public static int ActionCount => AiModelsConfig.HedgeRatioLevels.Length;
    public static int StateCount => 4 * AiModelsConfig.HedgeRatioLevels.Length * 2;
    public int Steps => config.Steps;

    public int State => Encode(_t, _hedgeIdx, _regime);

    public static int EncodeState(int timeBucket, int hedgeIndex, int regime) =>
        (Math.Clamp(timeBucket, 0, 3) * AiModelsConfig.HedgeRatioLevels.Length + hedgeIndex) * 2 + regime;

    private int Encode(int t, int h, int regime) => EncodeState(t * 4 / config.Steps, h, regime);

    public int Reset(int? newSeed = null, int startRegime = 0, int startHedgeIndex = 0)
    {
        if (newSeed is int s) _rng = new Rng(s);
        _t = 0;
        _hedgeIdx = startHedgeIndex;
        _regime = startRegime;
        return State;
    }

    public (int NextState, double Reward, double Pnl, bool Done) Step(int action)
    {
        var levels = AiModelsConfig.HedgeRatioLevels;
        double oldH = levels[_hedgeIdx];
        double newH = levels[action];
        double cost = config.CostPerUnit * Math.Abs(newH - oldH);
        double ret = config.SigmaOrDefault[_regime] * _rng.NextNormal();
        double exposed = (1 - newH) * ret;
        double pnl = exposed - cost;
        double sigma = config.SigmaOrDefault[_regime];
        double reward = -cost - config.RiskAversion * (1 - newH) * (1 - newH) * sigma * sigma;
        _hedgeIdx = action;
        var tr = config.TransitionOrDefault;
        _regime = _rng.NextDouble() < tr[_regime, 0] ? 0 : 1;
        _t++;
        return (State, reward, pnl, _t >= config.Steps);
    }
}
