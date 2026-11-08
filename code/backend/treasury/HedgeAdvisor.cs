using Ballast.AiModels.Agents;
using Ballast.AiModels.Config;
using Ballast.AiModels.Environments;
using Ballast.Core.Domain;

namespace Ballast.Treasury;

public sealed record HedgeRecommendation(
    string Currency, string Bucket, decimal TargetRatio, decimal CurrentRatio, HedgeDirection? Direction,
    decimal RecommendedNotional, double HighVolProbability, string Rationale);

public sealed class HedgeAdvisor(QLearningHedger agent)
{
    private static readonly (double Lo, double Hi)[] Bands = [(0.50, 0.90), (0.30, 0.75), (0.10, 0.50)];

    public IReadOnlyList<HedgeRecommendation> Recommend(
        IReadOnlyList<ExposureLine> exposures, IReadOnlyDictionary<string, double> highVolProbability)
    {
        var levels = AiModelsConfig.HedgeRatioLevels;
        var result = new List<HedgeRecommendation>();
        foreach (var e in exposures)
        {
            if (e.NetNative == 0) continue;
            double pHigh = highVolProbability.GetValueOrDefault(e.Currency, 0.0);
            int regime = pHigh >= 0.5 ? 1 : 0;
            double current = (double)Math.Min(1m, e.HedgeRatio);
            int hedgeIdx = Array.IndexOf(levels, levels.OrderBy(l => Math.Abs(l - current)).First());
            int state = FxHedgingEnvironment.EncodeState(e.BucketIndex, hedgeIdx, regime);
            double agentRatio = levels[agent.Greedy(state)];
            var (lo, hi) = Bands[Math.Min(e.BucketIndex, Bands.Length - 1)];
            double target = Math.Clamp(agentRatio, lo, hi);
            decimal absNet = Math.Abs(e.NetNative);
            decimal notional = Math.Max(0, (decimal)target * absNet - e.HedgedNative);
            var direction = e.NetNative > 0 ? HedgeDirection.SellForeign : HedgeDirection.BuyForeign;
            string regimeLabel = regime == 1 ? "high-volatility" : "low-volatility";
            string rationale = $"Agent policy in the {regimeLabel} regime suggests {agentRatio * 100:F0}%; policy band for {e.Bucket} is {lo * 100:F0}% to {hi * 100:F0}%, target {target * 100:F0}%";
            result.Add(new HedgeRecommendation(e.Currency, e.Bucket, (decimal)target, e.HedgeRatio,
                notional > 0 ? direction : null, Math.Round(notional, 0), pHigh, rationale));
        }
        return result;
    }
}
