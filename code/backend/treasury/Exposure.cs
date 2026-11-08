using Ballast.Core.Domain;
using Ballast.MarketData;

namespace Ballast.Treasury;

public sealed record ExposureBucket(string Label, int FromDay, int ToDay);

public sealed record ExposureLine(
    string Currency, string Bucket, int BucketIndex, decimal NetNative, decimal NetBase,
    decimal HedgedNative, decimal HedgeRatio, decimal OpenNative, decimal OpenBase);

public static class ExposureAggregator
{
    public static readonly IReadOnlyList<ExposureBucket> Buckets =
    [
        new("0-30 days", 0, 29), new("31-60 days", 30, 59), new("61-91 days", 60, 90)
    ];

    public static IReadOnlyList<ExposureLine> Compute(
        IReadOnlyDictionary<string, double[]> nativeForecast, DateOnly forecastStart,
        IReadOnlyList<Hedge> hedges, IRateProvider rates, string baseCurrency)
    {
        var lines = new List<ExposureLine>();
        foreach (var (currency, daily) in nativeForecast.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (currency == baseCurrency) continue;
            for (int b = 0; b < Buckets.Count; b++)
            {
                var bucket = Buckets[b];
                decimal net = 0;
                for (int d = bucket.FromDay; d <= Math.Min(bucket.ToDay, daily.Length - 1); d++) net += (decimal)daily[d];
                decimal hedged = 0;
                foreach (var h in hedges.Where(x => x.Status == HedgeStatus.Active && x.Currency == currency))
                {
                    int day = h.MaturityDate.DayNumber - forecastStart.DayNumber;
                    if (day < bucket.FromDay || day > bucket.ToDay) continue;
                    if (net > 0 && h.Direction == HedgeDirection.SellForeign) hedged += h.Notional;
                    else if (net < 0 && h.Direction == HedgeDirection.BuyForeign) hedged += h.Notional;
                }
                decimal abs = Math.Abs(net);
                decimal ratio = abs == 0 ? 0 : Math.Min(1.5m, hedged / abs);
                decimal open = Math.Sign(net) * Math.Max(0, abs - hedged);
                lines.Add(new ExposureLine(currency, bucket.Label, b, net,
                    FxMath.Convert(rates, net, currency, baseCurrency), hedged, ratio, open,
                    FxMath.Convert(rates, open, currency, baseCurrency)));
            }
        }
        return lines;
    }
}
