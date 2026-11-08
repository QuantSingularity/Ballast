using System.Text.Json;
using System.Text.Json.Serialization;
using Ballast.Core.Domain;

namespace Ballast.Data;

public sealed record StateSnapshot(
    IReadOnlyList<User> Users,
    IReadOnlyList<Payment> Payments,
    IReadOnlyList<Hedge> Hedges,
    IReadOnlyList<IntercompanyBalance> IntercompanyBalances,
    IReadOnlyList<NettingCycle> NettingCycles,
    IReadOnlyList<AuditEntry> Audit);

public sealed class BallastDb(string? dataPath)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _saveLock = new();

    public Repository<LegalEntity> Entities { get; } = new("Entity", e => e.Id);
    public Repository<BankAccount> Accounts { get; } = new("Account", a => a.Id);
    public Repository<DebtFacility> Facilities { get; } = new("Facility", f => f.Id);
    public Repository<Hedge> Hedges { get; } = new("Hedge", h => h.Id);
    public Repository<IntercompanyBalance> IntercompanyBalances { get; } = new("Intercompany balance", b => b.Id);
    public Repository<NettingCycle> NettingCycles { get; } = new("Netting cycle", c => c.Id);
    public Repository<Payment> Payments { get; } = new("Payment", p => p.Id);
    public Repository<User> Users { get; } = new("User", u => u.Id);

    public List<CashTransaction> Transactions { get; } = [];
    public List<AuditEntry> AuditEntries { get; } = [];
    public object AuditLock { get; } = new();

    private string? StatePath => dataPath is null ? null : Path.Combine(dataPath, "ballast-state.json");

    public bool TryLoadSnapshot()
    {
        var path = StatePath;
        if (path is null || !File.Exists(path)) return false;
        var snap = JsonSerializer.Deserialize<StateSnapshot>(File.ReadAllText(path), Json);
        if (snap is null) return false;
        Users.ReplaceAll(snap.Users);
        Payments.ReplaceAll(snap.Payments);
        Hedges.ReplaceAll(snap.Hedges);
        IntercompanyBalances.ReplaceAll(snap.IntercompanyBalances);
        NettingCycles.ReplaceAll(snap.NettingCycles);
        lock (AuditLock)
        {
            AuditEntries.Clear();
            AuditEntries.AddRange(snap.Audit);
        }
        return true;
    }

    public void SaveChanges()
    {
        var path = StatePath;
        if (path is null) return;
        lock (_saveLock)
        {
            Directory.CreateDirectory(dataPath!);
            StateSnapshot snap;
            lock (AuditLock)
            {
                snap = new StateSnapshot(Users.All(), Payments.All(), Hedges.All(), IntercompanyBalances.All(),
                    NettingCycles.All(), AuditEntries.ToList());
            }
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snap, Json));
            File.Move(tmp, path, overwrite: true);
        }
    }
}
