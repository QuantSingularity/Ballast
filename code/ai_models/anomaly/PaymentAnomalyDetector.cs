using Ballast.AiModels.Numerics;

namespace Ballast.AiModels.Anomaly;

public sealed record OutflowRecord(string AccountId, string Counterparty, double Amount, DateOnly Date);

public sealed record AnomalyAssessment(double Score, double Threshold, double RobustZ, bool NewCounterparty, bool Flagged, IReadOnlyList<string> Reasons);

public sealed class PaymentAnomalyDetector
{
    private readonly IsolationForest _forest = new(trees: 120, sampleSize: 256, seed: 11);
    private readonly Dictionary<string, double> _accountMedian = [];
    private readonly Dictionary<string, (double Median, double Mad, int Count)> _counterparty = [];
    private readonly HashSet<string> _known = [];
    private double _threshold = 1.0;
    private double _globalMedian = 1.0;

    public bool IsFitted { get; private set; }
    public double Threshold => _threshold;

    private static string CpKey(string account, string cp) => $"{account}|{cp}";

    public void Fit(IReadOnlyList<OutflowRecord> history)
    {
        if (history.Count < 50) throw new ArgumentException("Need at least 50 outflows", nameof(history));
        foreach (var g in history.GroupBy(h => h.AccountId))
            _accountMedian[g.Key] = Math.Max(1.0, Stats.Median(g.Select(r => Math.Abs(r.Amount)).ToArray()));
        _globalMedian = Math.Max(1.0, Stats.Median(history.Select(r => Math.Abs(r.Amount)).ToArray()));
        foreach (var g in history.GroupBy(h => CpKey(h.AccountId, h.Counterparty)))
        {
            var amounts = g.Select(r => Math.Abs(r.Amount)).ToArray();
            double med = Stats.Median(amounts);
            _counterparty[g.Key] = (med, Math.Max(Math.Max(1.0, 0.02 * med), Stats.Mad(amounts)), amounts.Length);
            _known.Add(g.Key);
        }
        var rows = history.Select(r => Features(r.AccountId, r.Counterparty, Math.Abs(r.Amount), r.Date, newCp: false)).ToList();
        _forest.Fit(rows);
        var scores = rows.Select(_forest.Score).ToArray();
        _threshold = Stats.Quantile(scores, 0.995);
        IsFitted = true;
    }

    private double[] Features(string account, string cp, double amount, DateOnly date, bool newCp)
    {
        double med = _accountMedian.GetValueOrDefault(account, _globalMedian);
        return
        [
            Math.Log(1 + amount),
            Math.Log((1 + amount) / (1 + med)),
            date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1 : 0,
            newCp ? 1 : 0,
            date.Day / 31.0
        ];
    }

    public AnomalyAssessment Assess(string accountId, string counterparty, double amount, DateOnly valueDate)
    {
        if (!IsFitted) throw new InvalidOperationException("Detector is not fitted");
        amount = Math.Abs(amount);
        bool isNew = !_known.Contains(CpKey(accountId, counterparty));
        double score = _forest.Score(Features(accountId, counterparty, amount, valueDate, isNew));
        double z = 0;
        bool routine = false;
        var reasons = new List<string>();
        if (_counterparty.TryGetValue(CpKey(accountId, counterparty), out var stat) && stat.Count >= 5)
        {
            z = 0.6745 * (amount - stat.Median) / stat.Mad;
            routine = Math.Abs(z) <= 3;
            if (Math.Abs(z) > 6) reasons.Add($"Amount deviates from this beneficiary's usual payments (robust z = {z:F1})");
        }
        double med = _accountMedian.GetValueOrDefault(accountId, _globalMedian);
        if (isNew && amount > 3 * med) reasons.Add("Large payment to a beneficiary with no payment history on this account");
        if (score > _threshold && !routine) reasons.Add($"Isolation score {score:F2} exceeds the {_threshold:F2} baseline");
        if (valueDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) reasons.Add("Value date falls on a weekend");
        bool flagged = reasons.Count > 0 && ((score > _threshold && !routine) || Math.Abs(z) > 6 || (isNew && amount > 3 * med));
        return new AnomalyAssessment(score, _threshold, z, isNew, flagged, reasons);
    }
}
