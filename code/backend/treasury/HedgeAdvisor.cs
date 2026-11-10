using Ballast.Core.Domain;

namespace Ballast.Treasury;

public sealed record HedgeRecommendation(
    string Currency, string Bucket, decimal TargetRatio, decimal CurrentRatio, HedgeDirection? Direction,
    decimal RecommendedNotional, double HighVolProbability, string Rationale);

public sealed record AgentQuery(int BucketIndex, double CurrentRatio, double HighVolProbability);

/// <summary>
/// Turns an agent's suggested hedge ratios into policy-bounded recommendations. The agent itself is supplied as a
/// function, so this class stays pure and is tested without the model service.
/// </summary>
public sealed class HedgeAdvisor(Func<IReadOnlyList<AgentQuery>, IReadOnlyList<double>> agentRatios)
{
    private static readonly (double Lo, double Hi)[] Bands = [(0.50, 0.90), (0.30, 0.75), (0.10, 0.50)];

    public IReadOnlyList<HedgeRecommendation> Recommend(
        IReadOnlyList<ExposureLine> exposures, IReadOnlyDictionary<string, double> highVolProbability)
    {
        var active = exposures.Where(e => e.NetNative != 0).ToList();
        if (active.Count == 0) return [];
        var queries = active
            .Select(e => new AgentQuery(e.BucketIndex, (double)Math.Min(1m, e.HedgeRatio), highVolProbability.GetValueOrDefault(e.Currency)))
            .ToList();
        var suggested = agentRatios(queries);
        if (suggested.Count != active.Count)
            throw new InvalidOperationException("The agent returned a different number of ratios than were requested");

        var result = new List<HedgeRecommendation>();
        for (int i = 0; i < active.Count; i++)
        {
            var e = active[i];
            double pHigh = queries[i].HighVolProbability;
            double agentRatio = suggested[i];
            var (lo, hi) = Bands[Math.Min(e.BucketIndex, Bands.Length - 1)];
            double target = Math.Clamp(agentRatio, lo, hi);
            decimal notional = Math.Max(0, (decimal)target * Math.Abs(e.NetNative) - e.HedgedNative);
            var direction = e.NetNative > 0 ? HedgeDirection.SellForeign : HedgeDirection.BuyForeign;
            string regimeLabel = pHigh >= 0.5 ? "high-volatility" : "low-volatility";
            string rationale = $"Agent policy in the {regimeLabel} regime suggests {agentRatio * 100:F0}%; policy band for {e.Bucket} is {lo * 100:F0}% to {hi * 100:F0}%, target {target * 100:F0}%";
            result.Add(new HedgeRecommendation(e.Currency, e.Bucket, (decimal)target, e.HedgeRatio,
                notional > 0 ? direction : null, Math.Round(notional, 0), pHigh, rationale));
        }
        return result;
    }
}
