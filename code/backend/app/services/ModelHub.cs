using System.Collections.Concurrent;
using Ballast.AiClient;
using Ballast.Core;
using Ballast.Data;
using Ballast.MarketData;
using Ballast.Treasury;

namespace Ballast.Api.Services;

/// <summary>
/// Orchestrates the Python model service: fits the anomaly detector once, caches regime models, and translates
/// between platform types and model requests.
/// </summary>
public sealed class ModelHub(BallastDb db, IRateProvider rates, BallastSettings settings, AiServiceClient ai)
{
    private readonly object _detectorLock = new();
    private AnomalyFit? _fit;
    private readonly ConcurrentDictionary<string, Lazy<HmmParameters>> _hmms = new();
    private readonly ConcurrentDictionary<string, Lazy<double>> _highVol = new();

    public bool DetectorAvailable => db.Transactions.Count(t => t.Amount < 0) >= 50;

    public double AnomalyThreshold
    {
        get
        {
            EnsureDetector();
            return _fit!.Threshold;
        }
    }

    private void EnsureDetector()
    {
        lock (_detectorLock)
        {
            if (_fit is not null) return;
            var accounts = db.Accounts.All().ToDictionary(a => a.Id);
            var history = db.Transactions.Where(t => t.Amount < 0 && accounts.ContainsKey(t.AccountId))
                .Select(t => new OutflowRecord(t.AccountId, t.Counterparty,
                    (double)Math.Abs(FxMath.Convert(rates, t.Amount, t.Currency, settings.BaseCurrency)), t.Date))
                .ToList();
            _fit = ai.FitAnomaly(history);
        }
    }

    public IReadOnlyList<AnomalyAssessment> AssessMany(IReadOnlyList<AssessItem> items)
    {
        if (!DetectorAvailable) throw new ConflictException("Not enough payment history to screen payments");
        if (items.Count == 0) return [];
        EnsureDetector();
        try
        {
            return ai.AssessBatch(items);
        }
        catch (ModelNotFittedException)
        {
            // The model service restarted and lost its in-memory detector. Fit it again and retry once.
            lock (_detectorLock) _fit = null;
            EnsureDetector();
            return ai.AssessBatch(items);
        }
    }

    public AnomalyAssessment Assess(string accountId, string counterparty, decimal amount, string currency, DateOnly valueDate)
    {
        double baseAmount = (double)Math.Abs(FxMath.Convert(rates, amount, currency, settings.BaseCurrency));
        return AssessMany([new AssessItem(accountId, counterparty, baseAmount, valueDate)])[0];
    }

    public HmmParameters Hmm(string currency) =>
        _hmms.GetOrAdd(currency, c => new Lazy<HmmParameters>(() => ai.FitRegime(rates.LogReturns(c, 750)))).Value;

    public double HighVolProbability(string currency) =>
        currency == "USD"
            ? 0
            : _highVol.GetOrAdd(currency, c => new Lazy<double>(() => ai.FilterRegime(Hmm(c), rates.LogReturns(c, 20))[1])).Value;

    public IReadOnlyList<double> AgentRatios(IReadOnlyList<AgentQuery> queries) =>
        ai.RecommendHedges(queries.Select(q => new HedgeRatioQuery(q.BucketIndex, q.CurrentRatio, q.HighVolProbability)).ToList()).Ratios;

    public FxVarResult FxVar(string currency, double exposureBase) =>
        ai.FxVar(Hmm(currency), rates.LogReturns(currency, 60), exposureBase, 21, 0.95, 31);

    public ModelStatus Models() => ai.Models();
}
