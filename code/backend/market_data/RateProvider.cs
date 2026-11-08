using Ballast.AiModels.Generative;
using Ballast.Core;
using Ballast.Core.Domain;

namespace Ballast.MarketData;

public interface IRateProvider
{
    decimal SpotUsd(string currency);

    double AnnualRate(string currency);

    IReadOnlyList<double> LogReturns(string currency, int days);

    IReadOnlyList<(DateOnly Date, decimal SpotUsd)> SpotHistory(string currency, int days);
}

public sealed class SyntheticRateProvider(DateOnly asOf) : IRateProvider
{
    private static readonly Dictionary<string, decimal> Spots = new()
    {
        ["USD"] = 1.0m,
        ["EUR"] = 1.0850m,
        ["GBP"] = 1.2900m,
        ["JPY"] = 0.006670m,
        ["CHF"] = 1.1200m
    };

    private static readonly Dictionary<string, double> Rates = new()
    {
        ["USD"] = 0.0400,
        ["EUR"] = 0.0225,
        ["GBP"] = 0.0375,
        ["JPY"] = 0.0075,
        ["CHF"] = 0.0025
    };

    private static readonly Dictionary<string, (double Calm, double Stress, int Seed)> Vol = new()
    {
        ["EUR"] = (0.0042, 0.0110, 101),
        ["GBP"] = (0.0046, 0.0125, 202),
        ["JPY"] = (0.0050, 0.0140, 303),
        ["CHF"] = (0.0040, 0.0100, 404)
    };

    private readonly Dictionary<string, double[]> _returnCache = [];
    private readonly object _lock = new();

    public decimal SpotUsd(string currency) =>
        Spots.TryGetValue(currency, out var s) ? s : throw new ValidationException($"Unsupported currency '{currency}'");

    public double AnnualRate(string currency) =>
        Rates.TryGetValue(currency, out var r) ? r : throw new ValidationException($"Unsupported currency '{currency}'");

    private double[] FullReturns(string currency)
    {
        lock (_lock)
        {
            if (_returnCache.TryGetValue(currency, out var cached)) return cached;
            double[] r;
            if (currency == "USD") r = new double[900];
            else
            {
                var v = Vol[currency];
                var sim = new RegimeSwitchingSimulator([0.0, 0.0], [v.Calm, v.Stress],
                    new[,] { { 0.97, 0.03 }, { 0.08, 0.92 } }, v.Seed);
                r = sim.SimulateReturns(1, 900)[0];
                if (currency is "GBP" or "JPY") for (int i = 840; i < 900; i++) r[i] *= 2.0;
            }
            _returnCache[currency] = r;
            return r;
        }
    }

    public IReadOnlyList<double> LogReturns(string currency, int days)
    {
        var all = FullReturns(currency);
        days = Math.Clamp(days, 1, all.Length);
        return all[^days..];
    }

    public IReadOnlyList<(DateOnly Date, decimal SpotUsd)> SpotHistory(string currency, int days)
    {
        var r = LogReturns(currency, days);
        var result = new (DateOnly, decimal)[r.Count];
        double level = (double)SpotUsd(currency);
        for (int i = r.Count - 1; i >= 0; i--)
        {
            result[i] = (asOf.AddDays(-(r.Count - 1 - i)), (decimal)level);
            level /= Math.Exp(r[i]);
        }
        return result;
    }
}

public static class FxMath
{
    public static decimal Convert(IRateProvider rates, decimal amount, string from, string to)
    {
        if (from == to) return amount;
        return amount * rates.SpotUsd(from) / rates.SpotUsd(to);
    }

    public static decimal ForwardUsd(IRateProvider rates, string currency, int tenorDays)
    {
        if (currency == "USD") return 1m;
        double t = Math.Max(0, tenorDays) / 360.0;
        double f = (double)rates.SpotUsd(currency) * (1 + rates.AnnualRate("USD") * t) / (1 + rates.AnnualRate(currency) * t);
        return (decimal)f;
    }

    public static decimal HedgeMarkToMarketUsd(IRateProvider rates, Hedge hedge, DateOnly asOf)
    {
        int remaining = Math.Max(0, hedge.MaturityDate.DayNumber - asOf.DayNumber);
        decimal forward = ForwardUsd(rates, hedge.Currency, remaining);
        decimal gross = hedge.Direction == HedgeDirection.SellForeign
            ? hedge.Notional * (hedge.ContractRate - forward)
            : hedge.Notional * (forward - hedge.ContractRate);
        double discount = 1 + rates.AnnualRate("USD") * remaining / 360.0;
        return gross / (decimal)discount;
    }
}
