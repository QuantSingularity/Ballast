using Ballast.Core;
using Ballast.Core.Domain;

namespace Ballast.Data;

public static class DemoSeeder
{
    public const int HistoryDays = 540;

    private sealed record AccountSpec(
        string Id, string EntityId, string Bank, string Currency, AccountKind Kind, double Scale, double Drift, decimal Target);

    private static readonly AccountSpec[] Specs =
    [
        new("ACC-US-001", "ENT-US", "Northern Trust", "USD", AccountKind.Operating, 1.4, -0.25, 5_600_000m),
        new("ACC-US-002", "ENT-US", "Citibank", "USD", AccountKind.Operating, 0.6, -0.24, 2_400_000m),
        new("ACC-US-003", "ENT-US", "Northern Trust", "USD", AccountKind.Savings, 0, 0, 3_000_000m),
        new("ACC-US-004", "ENT-US", "Citibank", "EUR", AccountKind.Operating, 0.25, -0.10, 1_000_000m),
        new("ACC-DE-001", "ENT-DE", "Deutsche Bank", "EUR", AccountKind.Operating, 1.0, -0.10, 4_000_000m),
        new("ACC-DE-002", "ENT-DE", "Commerzbank", "EUR", AccountKind.Operating, 0.5, -0.10, 2_000_000m),
        new("ACC-DE-003", "ENT-DE", "Deutsche Bank", "EUR", AccountKind.Savings, 0, 0, 1_800_000m),
        new("ACC-UK-001", "ENT-UK", "Barclays", "GBP", AccountKind.Operating, 0.8, -0.10, 3_200_000m),
        new("ACC-UK-002", "ENT-UK", "HSBC", "GBP", AccountKind.Operating, 0.35, -0.10, 1_400_000m),
        new("ACC-UK-003", "ENT-UK", "Barclays", "USD", AccountKind.Operating, 0.3, -0.24, 1_200_000m),
        new("ACC-JP-001", "ENT-JP", "MUFG", "JPY", AccountKind.Operating, 120, -0.10, 480_000_000m),
        new("ACC-JP-002", "ENT-JP", "SMBC", "JPY", AccountKind.Savings, 0, 0, 90_000_000m),
        new("ACC-CH-001", "ENT-CH", "UBS", "CHF", AccountKind.Operating, 0.7, -0.10, 2_800_000m),
        new("ACC-LU-001", "ENT-LU", "BNP Paribas", "EUR", AccountKind.Pool, 0, 0, 3_500_000m)
    ];


    public static void SeedStatic(BallastDb db, BallastSettings settings)
    {
        db.Entities.ReplaceAll(
        [
            new("ENT-US", "Ballast Holdings Inc", "US", "USD"),
            new("ENT-DE", "Ballast Europe GmbH", "DE", "EUR"),
            new("ENT-UK", "Ballast UK Ltd", "GB", "GBP"),
            new("ENT-JP", "Ballast Japan KK", "JP", "JPY"),
            new("ENT-CH", "Ballast Swiss AG", "CH", "CHF"),
            new("ENT-LU", "Ballast Treasury Services S.a r.l.", "LU", "EUR")
        ]);

        var today = settings.Today;
        var start = today.AddDays(-HistoryDays);
        var accounts = new List<BankAccount>();
        db.Transactions.Clear();
        int idx = 0;
        foreach (var spec in Specs)
        {
            idx++;
            decimal balance = spec.Target;
            if (spec.Kind == AccountKind.Operating)
            {
                var flows = SyntheticCashFlowGenerator.Generate(start, HistoryDays, new FlowProfile(spec.Scale, spec.Drift, 1000 + idx));
                int n = 0;
                foreach (var f in flows)
                {
                    decimal amt = Currencies.Round((decimal)f.Amount, spec.Currency);
                    db.Transactions.Add(new CashTransaction($"TX-{spec.Id}-{++n:D5}", spec.Id, f.Date, amt, spec.Currency, f.Category, f.Counterparty));
                }
            }
            accounts.Add(new BankAccount(spec.Id, spec.EntityId, spec.Bank, $"XX00 BLST {idx:D4} 0000 {idx * 37:D4}",
                spec.Currency, balance, spec.Kind));
        }
        db.Accounts.ReplaceAll(accounts);

        var floatingMaturity = today.AddYears(3);
        db.Facilities.ReplaceAll(
        [
            new("FAC-RCF", "ENT-US", "Syndicated Revolving Credit Facility", "USD", 100_000_000m, 78_000_000m, 0.0560m, true,
                floatingMaturity,
                [
                    new("COV-RCF-LIQ", "Minimum liquidity", CovenantKind.MinLiquidity, 55_000_000m),
                    new("COV-RCF-LEV", "Maximum net debt to EBITDA", CovenantKind.MaxNetDebtToEbitda, 3.5m),
                    new("COV-RCF-ICR", "Minimum interest cover", CovenantKind.MinInterestCover, 4.0m)
                ]),
            new("FAC-TLA", "ENT-DE", "Term Loan A", "EUR", 40_000_000m, 40_000_000m, 0.0410m, false, today.AddMonths(16),
                [new("COV-TLA-LEV", "Maximum net debt to EBITDA", CovenantKind.MaxNetDebtToEbitda, 3.5m)]),
            new("FAC-UKL", "ENT-UK", "UK Working Capital Line", "GBP", 5_000_000m, 1_200_000m, 0.0620m, true, today.AddMonths(11), [])
        ]);
    }

    public static void SeedMutable(BallastDb db, BallastSettings settings, Func<string, string> hashPassword)
    {
        var today = settings.Today;
        var now = new DateTimeOffset(today.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
        var hash = hashPassword(settings.DemoPassword);
        db.Users.ReplaceAll(
        [
            new("U-001", "admin@ballast.example", "Avery Admin", UserRole.Admin, hash, now),
            new("U-002", "treasurer@ballast.example", "Taylor Treasurer", UserRole.Treasurer, hash, now),
            new("U-003", "approver@ballast.example", "Alex Approver", UserRole.Approver, hash, now),
            new("U-004", "approver2@ballast.example", "Sam Approver", UserRole.Approver, hash, now),
            new("U-005", "analyst@ballast.example", "Jordan Analyst", UserRole.Analyst, hash, now)
        ]);

        db.Hedges.ReplaceAll(
        [
            new("HDG-0001", "EUR", HedgeDirection.SellForeign, 520_000m, 1.0920m, today.AddDays(-20), today.AddDays(25), "Deutsche Bank", HedgeStatus.Active),
            new("HDG-0002", "GBP", HedgeDirection.SellForeign, 400_000m, 1.2850m, today.AddDays(-15), today.AddDays(15), "Barclays", HedgeStatus.Active),
            new("HDG-0003", "JPY", HedgeDirection.SellForeign, 25_000_000m, 0.006700m, today.AddDays(-10), today.AddDays(50), "MUFG", HedgeStatus.Active),
            new("HDG-0004", "CHF", HedgeDirection.SellForeign, 150_000m, 1.1150m, today.AddDays(-30), today.AddDays(70), "UBS", HedgeStatus.Active)
        ]);

        db.IntercompanyBalances.ReplaceAll(
        [
            new("IC-001", "ENT-LU", "ENT-DE", "EUR", 2_000_000m),
            new("IC-002", "ENT-LU", "ENT-UK", "GBP", 1_500_000m),
            new("IC-003", "ENT-US", "ENT-JP", "USD", 3_000_000m),
            new("IC-004", "ENT-CH", "ENT-LU", "CHF", 1_200_000m),
            new("IC-005", "ENT-DE", "ENT-CH", "EUR", 800_000m),
            new("IC-006", "ENT-JP", "ENT-US", "JPY", 150_000_000m)
        ]);

        PaymentApproval Approval(string id, string name) => new(id, name, now);
        db.Payments.ReplaceAll(
        [
            new("PAY-1001", "ACC-DE-001", "Globex Materials", 28_400m, "EUR", "INV-4471", today.AddDays(1),
                PaymentStatus.PendingApproval, "U-002", now, [], [], 1, null),
            new("PAY-1002", "ACC-US-001", "Stark Freight", 185_000m, "USD", "INV-8812", today.AddDays(2),
                PaymentStatus.PendingApproval, "U-002", now, [Approval("U-003", "Alex Approver")], [], 2, null),
            new("PAY-1003", "ACC-UK-001", "Hooli Cloud", 12_750m, "GBP", "INV-2290", today.AddDays(-3),
                PaymentStatus.Released, "U-002", now.AddDays(-4), [Approval("U-003", "Alex Approver")], [], 1, null),
            new("PAY-1004", "ACC-US-002", "Wayne Utilities", 9_300m, "USD", "INV-5530", today.AddDays(3),
                PaymentStatus.Draft, "U-002", now, [], [], 1, null),
            new("PAY-1005", "ACC-US-001", "Meridian Advisory Partners", 640_000m, "USD", "CONSULT-0092", today.AddDays(2),
                PaymentStatus.PendingApproval, "U-002", now,  [],
                ["Anomaly screening: Large payment to a beneficiary with no payment history on this account"], 2, null)
        ]);
    }
}
