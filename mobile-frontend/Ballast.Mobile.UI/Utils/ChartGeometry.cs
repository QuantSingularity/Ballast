using System.Globalization;
using Ballast.Mobile.UI.Models;

namespace Ballast.Mobile.UI.Utils;

public sealed record AxisTick(double Position, string Label);

public sealed record ChartGeometry(
    string LinePath, string BandPath, IReadOnlyList<AxisTick> YTicks, IReadOnlyList<AxisTick> XTicks, double? ZeroY)
{
    private const double MarginLeft = 48, MarginRight = 10, MarginTop = 10, MarginBottom = 24;

    public static ChartGeometry? Build(IReadOnlyList<ChartPoint> points, double width, double height)
    {
        if (points.Count == 0) return null;
        double min = double.MaxValue, max = double.MinValue;
        foreach (var p in points)
        {
            min = Math.Min(min, Math.Min(p.Value, p.Lo ?? p.Value));
            max = Math.Max(max, Math.Max(p.Value, p.Hi ?? p.Value));
        }
        if (min == max) { min -= 1; max += 1; }
        var ticks = NiceTicks(min, max, 5);
        double lo = ticks[0], hi = ticks[^1];
        double innerW = width - MarginLeft - MarginRight, innerH = height - MarginTop - MarginBottom;
        double X(int i) => MarginLeft + (points.Count == 1 ? innerW / 2 : i / (double)(points.Count - 1) * innerW);
        double Y(double v) => MarginTop + (1 - (v - lo) / (hi - lo == 0 ? 1 : hi - lo)) * innerH;

        string line = Path(points.Select((p, i) => (X(i), Y(p.Value))));
        bool hasBand = points.Any(p => p.Lo.HasValue && p.Hi.HasValue);
        string band = "";
        if (hasBand)
        {
            var upper = points.Select((p, i) => (X(i), Y(p.Hi ?? p.Value))).ToList();
            var lower = points.Select((p, i) => (X(i), Y(p.Lo ?? p.Value))).Reverse().ToList();
            band = Path(upper) + " " + string.Join(" ", lower.Select(t => $"L{F(t.Item1)} {F(t.Item2)}")) + " Z";
        }
        int step = Math.Max(1, (int)Math.Round(points.Count / 4.0));
        var xTicks = points.Select((p, i) => (p, i)).Where(t => t.i % step == 0).Select(t => new AxisTick(X(t.i), t.p.Label)).ToList();
        return new ChartGeometry(line, band, ticks.Select(t => new AxisTick(Y(t), Formatters.Compact(t))).ToList(), xTicks,
            lo < 0 && hi > 0 ? Y(0) : null);
    }

    public static IReadOnlyList<double> NiceTicks(double min, double max, int count = 5)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max)) return [0];
        if (min == max) return [min];
        double raw = (max - min) / Math.Max(1, count - 1);
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double norm = raw / mag;
        double step = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10) * mag;
        double start = Math.Floor(min / step) * step;
        var ticks = new List<double>();
        for (double v = start; v <= max + step * 0.5; v += step) ticks.Add(Math.Round(v / step) * step);
        return ticks;
    }

    private static string F(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

    private static string Path(IEnumerable<(double X, double Y)> pts) =>
        string.Join(" ", pts.Select((p, i) => $"{(i == 0 ? "M" : "L")}{F(p.X)} {F(p.Y)}"));
}
