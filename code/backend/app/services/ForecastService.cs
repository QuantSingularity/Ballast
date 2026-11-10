using System.Collections.Concurrent;
using Ballast.AiClient;
using Ballast.Core;
using Ballast.Core.Domain;
using Ballast.Data;
using Ballast.MarketData;

namespace Ballast.Api.Services;

public sealed record ForecastScope(string Key, IReadOnlyList<string> AccountIds, string? NativeCurrency, string OutputCurrency);

public sealed class ForecastService(BallastDb db, IRateProvider rates, BallastSettings settings, AiServiceClient ai)
{
    private readonly ConcurrentDictionary<string, Lazy<ForecastResult>> _forecasts = new();
    private readonly ConcurrentDictionary<string, Lazy<BacktestMetrics>> _backtests = new();
    private readonly Lazy<Dictionary<string, double[]>> _accountSeries = new(() => BuildAccountSeries(db, settings));

    public int HistoryDays => DemoSeeder.HistoryDays;

    public DateOnly HistoryStart => settings.Today.AddDays(-HistoryDays);

    private static Dictionary<string, double[]> BuildAccountSeries(BallastDb db, BallastSettings settings)
    {
        var start = settings.Today.AddDays(-DemoSeeder.HistoryDays);
        var result = new Dictionary<string, double[]>();
        foreach (var g in db.Transactions.GroupBy(t => t.AccountId))
        {
            var series = new double[DemoSeeder.HistoryDays];
            foreach (var t in g)
            {
                int idx = t.Date.DayNumber - start.DayNumber;
                if (idx >= 0 && idx < series.Length) series[idx] += (double)t.Amount;
            }
            result[g.Key] = series;
        }
        return result;
    }

    private IReadOnlyList<BankAccount> OperatingAccounts() =>
        db.Accounts.All().Where(a => a.Kind == AccountKind.Operating && _accountSeries.Value.ContainsKey(a.Id)).ToList();

    public IReadOnlyList<string> ForeignCurrencies() =>
        OperatingAccounts().Select(a => a.Currency).Where(c => c != settings.BaseCurrency).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();

    public ForecastScope ResolveScope(string? text)
    {
        text = string.IsNullOrWhiteSpace(text) ? "all" : text.Trim();
        var operating = OperatingAccounts();
        if (text.Equals("all", StringComparison.OrdinalIgnoreCase))
            return new ForecastScope("all", operating.Select(a => a.Id).ToList(), null, settings.BaseCurrency);
        if (text.StartsWith("currency:", StringComparison.OrdinalIgnoreCase))
        {
            var ccy = text[9..].ToUpperInvariant();
            if (!Currencies.IsSupported(ccy)) throw new ValidationException($"Unsupported currency '{ccy}'");
            var ids = operating.Where(a => a.Currency == ccy).Select(a => a.Id).ToList();
            if (ids.Count == 0) throw new NotFoundException($"No operating accounts in {ccy}");
            return new ForecastScope($"currency:{ccy}", ids, ccy, ccy);
        }
        if (text.StartsWith("account:", StringComparison.OrdinalIgnoreCase))
        {
            var id = text[8..];
            var acc = operating.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new NotFoundException($"Operating account '{id}' was not found");
            return new ForecastScope($"account:{acc.Id}", [acc.Id], acc.Currency, acc.Currency);
        }
        throw new ValidationException("Scope must be 'all', 'currency:XXX' or 'account:ID'");
    }

    public double[] Series(ForecastScope scope)
    {
        var total = new double[HistoryDays];
        foreach (var id in scope.AccountIds)
        {
            var acc = db.Accounts.Get(id);
            double factor = scope.NativeCurrency is null
                ? (double)(rates.SpotUsd(acc.Currency) / rates.SpotUsd(settings.BaseCurrency))
                : 1.0;
            var s = _accountSeries.Value[id];
            for (int i = 0; i < total.Length; i++) total[i] += s[i] * factor;
        }
        return total;
    }

    public ForecastResult Forecast(ForecastScope scope, int horizonDays, double confidence, int origins = 20)
    {
        if (horizonDays is < 7 or > 180) throw new ValidationException("horizonDays must be between 7 and 180");
        if (confidence is < 0.5 or > 0.99) throw new ValidationException("confidence must be between 0.5 and 0.99");
        if (scope.AccountIds.Count == 0) throw new ConflictException("No cash history is available to forecast");
        var key = $"{scope.Key}|{horizonDays}|{confidence:F3}|{origins}";
        return _forecasts.GetOrAdd(key, _ => new Lazy<ForecastResult>(() =>
            ai.Forecast(Series(scope), HistoryStart, horizonDays, confidence, origins))).Value;
    }

    public BacktestMetrics Backtest(ForecastScope scope, int horizonDays)
    {
        if (horizonDays is < 7 or > 91) throw new ValidationException("horizon must be between 7 and 91");
        if (scope.AccountIds.Count == 0) throw new ConflictException("No cash history is available to backtest");
        return _backtests.GetOrAdd($"{scope.Key}|{horizonDays}", _ => new Lazy<BacktestMetrics>(() =>
            ai.Backtest(Series(scope), HistoryStart, horizonDays, 8, 20, 0.9))).Value;
    }
}
