using Ballast.Core;
using Ballast.Core.Domain;
using Ballast.MarketData;

namespace Ballast.Treasury;

public sealed record NewPayment(string AccountId, string Beneficiary, decimal Amount, string Reference, DateOnly ValueDate);

public sealed record Actor(string Id, string Name, UserRole Role);

public sealed class PaymentWorkflow(BallastSettings settings, IRateProvider rates, TimeProvider clock)
{
    private static bool IsMaker(UserRole r) => r is UserRole.Treasurer or UserRole.Admin;

    private static bool IsChecker(UserRole r) => r is UserRole.Approver or UserRole.Admin;

    public Payment Create(
        NewPayment req, BankAccount account, Actor actor, string id,
        IReadOnlyList<string> screeningFlags, bool anomalyFlagged, bool duplicateSuspected)
    {
        if (!IsMaker(actor.Role)) throw new ForbiddenException("Only treasurers and administrators can create payments");
        if (req.Amount <= 0) throw new ValidationException("Amount must be greater than zero");
        if (string.IsNullOrWhiteSpace(req.Beneficiary)) throw new ValidationException("Beneficiary is required");
        if (req.ValueDate < settings.Today) throw new ValidationException("Value date cannot be in the past");
        if (account.Kind is AccountKind.Escrow) throw new ValidationException("Payments cannot be made from escrow accounts");

        decimal baseAmount = FxMath.Convert(rates, req.Amount, account.Currency, settings.BaseCurrency);
        int required = baseAmount > settings.DualApprovalThreshold ? 2 : 1;
        var flags = screeningFlags.ToList();
        if (duplicateSuspected) flags.Add("Possible duplicate: same beneficiary and amount within 7 days");
        if (anomalyFlagged || duplicateSuspected) required = 2;
        return new Payment(id, account.Id, req.Beneficiary.Trim(), Currencies.Round(req.Amount, account.Currency), account.Currency,
            req.Reference?.Trim() ?? "", req.ValueDate, PaymentStatus.Draft, actor.Id, clock.GetUtcNow(), [], flags, required, null);
    }

    public Payment Submit(Payment p, Actor actor)
    {
        if (p.Status != PaymentStatus.Draft) throw new ConflictException($"Payment is {p.Status}, only drafts can be submitted");
        if (actor.Id != p.CreatedBy && actor.Role != UserRole.Admin) throw new ForbiddenException("Only the creator can submit this payment");
        return p with { Status = PaymentStatus.PendingApproval };
    }

    public Payment Approve(Payment p, Actor actor)
    {
        if (!IsChecker(actor.Role)) throw new ForbiddenException("Only approvers and administrators can approve payments");
        if (p.Status != PaymentStatus.PendingApproval) throw new ConflictException($"Payment is {p.Status}, not pending approval");
        if (actor.Id == p.CreatedBy) throw new ForbiddenException("Maker-checker: the creator cannot approve their own payment");
        if (p.Approvals.Any(a => a.UserId == actor.Id)) throw new ConflictException("This approver has already approved the payment");
        var approvals = p.Approvals.Append(new PaymentApproval(actor.Id, actor.Name, clock.GetUtcNow())).ToList();
        var status = approvals.Count >= p.RequiredApprovals ? PaymentStatus.Approved : PaymentStatus.PendingApproval;
        return p with { Approvals = approvals, Status = status };
    }

    public Payment Reject(Payment p, Actor actor, string reason)
    {
        if (!IsChecker(actor.Role)) throw new ForbiddenException("Only approvers and administrators can reject payments");
        if (p.Status != PaymentStatus.PendingApproval) throw new ConflictException($"Payment is {p.Status}, not pending approval");
        if (string.IsNullOrWhiteSpace(reason)) throw new ValidationException("A rejection reason is required");
        return p with { Status = PaymentStatus.Rejected, RejectionReason = reason.Trim() };
    }

    public Payment Release(Payment p, Actor actor, BankAccount account)
    {
        if (!IsMaker(actor.Role)) throw new ForbiddenException("Only treasurers and administrators can release payments");
        if (p.Status != PaymentStatus.Approved) throw new ConflictException($"Payment is {p.Status}, only approved payments can be released");
        if (p.Approvals.Any(a => a.UserId == actor.Id)) throw new ForbiddenException("The releaser must be different from the approvers");
        if (account.Balance < p.Amount) throw new ConflictException("Insufficient balance on the debit account");
        return p with { Status = PaymentStatus.Released };
    }
}
