namespace Ballast.Core.Domain;

public enum AccountKind { Operating, Savings, Pool, Escrow }

public enum HedgeDirection { SellForeign, BuyForeign }

public enum HedgeStatus { Active, Matured, Cancelled }

public enum PaymentStatus { Draft, PendingApproval, Approved, Released, Rejected }

public enum UserRole { Analyst, Treasurer, Approver, Admin }

public enum CovenantKind { MinLiquidity, MaxNetDebtToEbitda, MinInterestCover }

public sealed record LegalEntity(string Id, string Name, string Country, string FunctionalCurrency);

public sealed record BankAccount(
    string Id, string EntityId, string Bank, string Iban, string Currency, decimal Balance, AccountKind Kind);

public sealed record CashTransaction(
    string Id, string AccountId, DateOnly Date, decimal Amount, string Currency, string Category, string Counterparty);

public sealed record Hedge(
    string Id, string Currency, HedgeDirection Direction, decimal Notional, decimal ContractRate,
    DateOnly TradeDate, DateOnly MaturityDate, string Counterparty, HedgeStatus Status);

public sealed record Covenant(string Id, string Name, CovenantKind Kind, decimal Threshold);

public sealed record DebtFacility(
    string Id, string EntityId, string Name, string Currency, decimal Commitment, decimal Drawn,
    decimal AnnualRate, bool Floating, DateOnly Maturity, IReadOnlyList<Covenant> Covenants);

public sealed record IntercompanyBalance(
    string Id, string LenderEntityId, string BorrowerEntityId, string Currency, decimal Amount);

public sealed record NettingTransfer(string FromEntityId, string ToEntityId, decimal Amount);

public sealed record NettingCycle(
    string Id, DateTimeOffset RunAt, string RunBy, string SettlementCurrency,
    decimal GrossVolume, decimal NetVolume, IReadOnlyList<NettingTransfer> Transfers);

public sealed record PaymentApproval(string UserId, string UserName, DateTimeOffset At);

public sealed record Payment(
    string Id, string AccountId, string Beneficiary, decimal Amount, string Currency, string Reference,
    DateOnly ValueDate, PaymentStatus Status, string CreatedBy, DateTimeOffset CreatedAt,
    IReadOnlyList<PaymentApproval> Approvals, IReadOnlyList<string> RiskFlags, int RequiredApprovals,
    string? RejectionReason);

public sealed record User(
    string Id, string Email, string Name, UserRole Role, string PasswordHash, DateTimeOffset CreatedAt);

public sealed record AuditEntry(
    long Sequence, DateTimeOffset Timestamp, string Actor, string Action, string Subject, string Detail,
    string PrevHash, string Hash);

public static class Currencies
{
    public static readonly IReadOnlyList<string> All = ["USD", "EUR", "GBP", "JPY", "CHF"];

    public static bool IsSupported(string code) => All.Contains(code, StringComparer.Ordinal);

    public static int Decimals(string code) => code == "JPY" ? 0 : 2;

    public static decimal Round(decimal amount, string code) =>
        Math.Round(amount, Decimals(code), MidpointRounding.ToEven);
}
