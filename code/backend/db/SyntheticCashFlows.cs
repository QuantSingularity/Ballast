using Ballast.Core;

namespace Ballast.Data;

public sealed record SyntheticFlow(DateOnly Date, string Category, string Counterparty, double Amount);

public sealed record FlowProfile(double Scale = 1.0, double NetDrift = 0.0, int Seed = 1);

public static class SyntheticCashFlowGenerator
{
    public static readonly string[] Customers =
        ["Northwind Retail", "Contoso Wholesale", "Fabrikam Industrial", "Adventure Works", "Tailspin Logistics", "Wingtip Online"];

    public static readonly string[] Suppliers =
        ["Globex Materials", "Initech Components", "Umbrella Packaging", "Stark Freight", "Wayne Utilities", "Hooli Cloud"];

    private static bool IsWeekend(DateOnly d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static DateOnly RollBack(DateOnly d)
    {
        while (IsWeekend(d)) d = d.AddDays(-1);
        return d;
    }

    private static DateOnly RollForward(DateOnly d)
    {
        while (IsWeekend(d)) d = d.AddDays(1);
        return d;
    }

    public static List<SyntheticFlow> Generate(DateOnly start, int days, FlowProfile profile)
    {
        var rng = new DeterministicRng(profile.Seed);
        var flows = new List<SyntheticFlow>(days * 6);
        double scale = profile.Scale;
        for (int i = 0; i < days; i++)
        {
            var date = start.AddDays(i);
            int dom = date.Day;
            int dim = DateTime.DaysInMonth(date.Year, date.Month);
            if (!IsWeekend(date))
            {
                int receipts = 2 + rng.NextInt(3);
                double boost = dom >= 26 ? 1.35 : 1.0;
                for (int r = 0; r < receipts; r++)
                {
                    double amt = scale * 25_000 * Math.Exp(0.5 * rng.NextNormal()) * boost * (1 + profile.NetDrift);
                    flows.Add(new SyntheticFlow(date, "Receipts", Customers[rng.NextInt(Customers.Length)], amt));
                }
                int pays = 1 + rng.NextInt(3);
                double mid = dom is >= 10 and <= 20 ? 1.2 : 1.0;
                for (int p = 0; p < pays; p++)
                {
                    double amt = -scale * 21_000 * Math.Exp(0.45 * rng.NextNormal()) * mid;
                    flows.Add(new SyntheticFlow(date, "Suppliers", Suppliers[rng.NextInt(Suppliers.Length)], amt));
                }
            }
            if (date == RollBack(new DateOnly(date.Year, date.Month, Math.Min(25, dim))))
                flows.Add(new SyntheticFlow(date, "Payroll", "Payroll Run", -scale * 180_000 * (1 + 0.02 * rng.NextNormal())));
            if (date == RollForward(new DateOnly(date.Year, date.Month, 1)))
                flows.Add(new SyntheticFlow(date, "Rent", "Property Holdings", -scale * 45_000));
            if (date.Month % 3 == 0 && date == RollForward(new DateOnly(date.Year, date.Month, 15)))
                flows.Add(new SyntheticFlow(date, "Tax", "Revenue Authority", -scale * 120_000 * (1 + 0.1 * rng.NextNormal())));
            if (dom == dim)
                flows.Add(new SyntheticFlow(date, "Fees", "Bank Charges", -scale * 3_500));
        }
        return flows;
    }

    public static double[] DailyNet(IEnumerable<SyntheticFlow> flows, DateOnly start, int days)
    {
        var y = new double[days];
        foreach (var f in flows)
        {
            int idx = f.Date.DayNumber - start.DayNumber;
            if (idx >= 0 && idx < days) y[idx] += f.Amount;
        }
        return y;
    }
}
