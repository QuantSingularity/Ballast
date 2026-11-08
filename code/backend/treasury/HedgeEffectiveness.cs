namespace Ballast.Treasury;

public enum EffectivenessStatus { Effective, Ineffective, NotAssessable }

public sealed record EffectivenessResult(decimal? Ratio, EffectivenessStatus Status);

public static class HedgeEffectiveness
{
    public const decimal LowerBound = 0.80m;
    public const decimal UpperBound = 1.25m;

    public static EffectivenessResult DollarOffset(decimal hedgeFairValueChange, decimal hedgedItemChange, decimal minAbsChange)
    {
        if (Math.Abs(hedgedItemChange) < minAbsChange) return new EffectivenessResult(null, EffectivenessStatus.NotAssessable);
        decimal ratio = -hedgeFairValueChange / hedgedItemChange;
        var status = ratio >= LowerBound && ratio <= UpperBound ? EffectivenessStatus.Effective : EffectivenessStatus.Ineffective;
        return new EffectivenessResult(ratio, status);
    }
}
