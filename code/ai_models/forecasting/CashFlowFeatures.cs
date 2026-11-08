namespace Ballast.AiModels.Forecasting;

public static class CashFlowFeatures
{
    public const int MinHistory = 28;

    public static double[] Build(DateOnly date, IReadOnlyList<double> series, int t)
    {
        var f = new List<double>(24);
        for (int d = 0; d < 7; d++) f.Add((int)date.DayOfWeek == d ? 1 : 0);
        int dom = date.Day;
        int bucket = dom <= 5 ? 0 : dom <= 10 ? 1 : dom <= 15 ? 2 : dom <= 20 ? 3 : dom <= 25 ? 4 : dom <= 28 ? 5 : 6;
        for (int b = 0; b < 7; b++) f.Add(bucket == b ? 1 : 0);
        int daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
        f.Add(dom == daysInMonth ? 1 : 0);
        f.Add(dom == 1 ? 1 : 0);
        f.Add(series[t - 1]);
        f.Add(series[t - 7]);
        f.Add(series[t - 14]);
        double s = 0;
        for (int i = 1; i <= 28; i++) s += series[t - i];
        f.Add(s / 28.0);
        return f.ToArray();
    }
}
