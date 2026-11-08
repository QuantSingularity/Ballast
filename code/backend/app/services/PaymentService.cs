using Ballast.Api.Schemas;
using Ballast.Core;
using Ballast.Core.Domain;
using Ballast.Data;
using Ballast.Infrastructure;
using Ballast.Treasury;

namespace Ballast.Api.Services;

public sealed class PaymentService(
    BallastDb db, PaymentWorkflow workflow, ModelHub models, AuditLog audit)
{
    private readonly object _lock = new();

    public IReadOnlyList<Payment> List(string? status)
    {
        var all = db.Payments.All().OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id, StringComparer.Ordinal).ToList();
        if (string.IsNullOrWhiteSpace(status)) return all;
        if (!Enum.TryParse<PaymentStatus>(status, true, out var s)) throw new ValidationException($"Unknown status '{status}'");
        return all.Where(p => p.Status == s).ToList();
    }

    public Payment Get(string id) => db.Payments.Get(id);

    public Payment Create(CreatePaymentRequest req, Actor actor)
    {
        if (string.IsNullOrWhiteSpace(req.AccountId)) throw new ValidationException("AccountId is required");
        var account = db.Accounts.Get(req.AccountId);
        var flags = new List<string>();
        bool anomaly = false;
        if (models.DetectorAvailable && account.Kind == AccountKind.Operating && req.Amount > 0 && !string.IsNullOrWhiteSpace(req.Beneficiary))
        {
            var a = models.Assess(account.Id, req.Beneficiary.Trim(), req.Amount, account.Currency, req.ValueDate);
            anomaly = a.Flagged;
            if (a.Flagged) flags.AddRange(a.Reasons.Select(r => $"Anomaly screening: {r}"));
        }
        bool duplicate = db.Payments.All().Any(p =>
            p.AccountId == account.Id && p.Status != PaymentStatus.Rejected &&
            string.Equals(p.Beneficiary, req.Beneficiary?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            p.Amount == Currencies.Round(req.Amount, account.Currency) &&
            Math.Abs(p.ValueDate.DayNumber - req.ValueDate.DayNumber) <= 7);

        Payment payment;
        lock (_lock)
        {
            string id = $"PAY-{1001 + db.Payments.Count}";
            payment = workflow.Create(new NewPayment(account.Id, req.Beneficiary ?? "", req.Amount, req.Reference ?? "", req.ValueDate),
                account, actor, id, flags, anomaly, duplicate);
            db.Payments.Upsert(payment);
        }
        audit.Append(actor.Name, "payment.create", payment.Id,
            $"{payment.Amount} {payment.Currency} to {payment.Beneficiary}; approvals required {payment.RequiredApprovals}; flags {payment.RiskFlags.Count}");
        db.SaveChanges();
        return payment;
    }

    private Payment Apply(string id, string action, Actor actor, Func<Payment, Payment> transition, string detail)
    {
        Payment updated;
        lock (_lock)
        {
            updated = transition(db.Payments.Get(id));
            db.Payments.Upsert(updated);
        }
        audit.Append(actor.Name, action, id, $"{detail}; status {updated.Status}");
        db.SaveChanges();
        return updated;
    }

    public Payment Submit(string id, Actor actor) => Apply(id, "payment.submit", actor, p => workflow.Submit(p, actor), "Submitted for approval");

    public Payment Approve(string id, Actor actor) =>
        Apply(id, "payment.approve", actor, p => workflow.Approve(p, actor), "Approval recorded");

    public Payment Reject(string id, string? reason, Actor actor) =>
        Apply(id, "payment.reject", actor, p => workflow.Reject(p, actor, reason ?? ""), $"Rejected: {reason}");

    public Payment Release(string id, Actor actor) =>
        Apply(id, "payment.release", actor, p => workflow.Release(p, actor, db.Accounts.Get(p.AccountId)), "Released to bank");

    public AiAssessment Assess(AssessPaymentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.AccountId) || string.IsNullOrWhiteSpace(req.Beneficiary))
            throw new ValidationException("AccountId and Beneficiary are required");
        if (req.Amount <= 0) throw new ValidationException("Amount must be greater than zero");
        var account = db.Accounts.Get(req.AccountId);
        var a = models.Assess(account.Id, req.Beneficiary.Trim(), req.Amount, account.Currency, req.ValueDate);
        return new AiAssessment(Math.Round(a.Score, 4), Math.Round(a.Threshold, 4), Math.Round(a.RobustZ, 2), a.NewCounterparty, a.Flagged, a.Reasons);
    }
}

public sealed record AiAssessment(double Score, double Threshold, double RobustZ, bool NewCounterparty, bool Flagged, IReadOnlyList<string> Reasons);
