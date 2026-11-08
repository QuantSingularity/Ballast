using System.Collections.Concurrent;
using Ballast.AiModels.Agents;
using Ballast.AiModels.Anomaly;
using Ballast.AiModels.Environments;
using Ballast.AiModels.Generative;
using Ballast.Core;
using Ballast.Core.Domain;
using Ballast.Data;
using Ballast.MarketData;

namespace Ballast.Api.Services;

public sealed class ModelHub(BallastDb db, IRateProvider rates, BallastSettings settings)
{
    private readonly Lazy<PaymentAnomalyDetector> _detector = new(() => TrainDetector(db, rates, settings), LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly Lazy<QLearningHedger> _agent = new(TrainAgent, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly ConcurrentDictionary<string, Lazy<GaussianHmm>> _hmms = new();

    public PaymentAnomalyDetector Detector => _detector.Value;

    public QLearningHedger Agent => _agent.Value;

    public bool DetectorAvailable => db.Transactions.Count(t => t.Amount < 0) >= 50;

    private static PaymentAnomalyDetector TrainDetector(BallastDb db, IRateProvider rates, BallastSettings settings)
    {
        var accounts = db.Accounts.All().ToDictionary(a => a.Id);
        var history = db.Transactions.Where(t => t.Amount < 0 && accounts.ContainsKey(t.AccountId))
            .Select(t => new OutflowRecord(t.AccountId, t.Counterparty,
                (double)Math.Abs(FxMath.Convert(rates, t.Amount, t.Currency, settings.BaseCurrency)), t.Date))
            .ToList();
        var detector = new PaymentAnomalyDetector();
        detector.Fit(history);
        return detector;
    }

    private static QLearningHedger TrainAgent()
    {
        var agent = new QLearningHedger();
        agent.Train(new FxHedgingEnvironment(new FxHedgingConfig(), 17), 25_000, 17);
        return agent;
    }

    public GaussianHmm Hmm(string currency) =>
        _hmms.GetOrAdd(currency, c => new Lazy<GaussianHmm>(() => GaussianHmm.Fit(rates.LogReturns(c, 750)))).Value;

    public double HighVolProbability(string currency) =>
        currency == "USD" ? 0 : Hmm(currency).FilterLast(rates.LogReturns(currency, 20).ToArray())[1];

    public AnomalyAssessment Assess(string accountId, string counterparty, decimal amount, string currency, DateOnly valueDate)
    {
        if (!DetectorAvailable) throw new ConflictException("Not enough payment history to screen payments");
        double baseAmount = (double)Math.Abs(FxMath.Convert(rates, amount, currency, settings.BaseCurrency));
        return Detector.Assess(accountId, counterparty, baseAmount, valueDate);
    }
}
