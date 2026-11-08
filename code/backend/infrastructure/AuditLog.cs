using System.Security.Cryptography;
using System.Text;
using Ballast.Core.Domain;
using Ballast.Data;

namespace Ballast.Infrastructure;

public sealed record AuditVerification(bool Valid, int Entries, long? BrokenAtSequence);

public sealed class AuditLog(BallastDb db, TimeProvider clock)
{
    public static readonly string GenesisHash = new('0', 64);

    private static string ComputeHash(string prev, long seq, DateTimeOffset ts, string actor, string action, string subject, string detail)
    {
        var input = $"{prev}|{seq}|{ts:O}|{actor}|{action}|{subject}|{detail}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    public AuditEntry Append(string actor, string action, string subject, string detail)
    {
        lock (db.AuditLock)
        {
            var prev = db.AuditEntries.Count == 0 ? GenesisHash : db.AuditEntries[^1].Hash;
            long seq = db.AuditEntries.Count + 1;
            var ts = clock.GetUtcNow();
            var entry = new AuditEntry(seq, ts, actor, action, subject, detail, prev,
                ComputeHash(prev, seq, ts, actor, action, subject, detail));
            db.AuditEntries.Add(entry);
            return entry;
        }
    }

    public IReadOnlyList<AuditEntry> Recent(int limit)
    {
        lock (db.AuditLock) return db.AuditEntries.TakeLast(limit).Reverse().ToList();
    }

    public AuditVerification Verify()
    {
        lock (db.AuditLock)
        {
            string prev = GenesisHash;
            foreach (var e in db.AuditEntries)
            {
                var expected = ComputeHash(prev, e.Sequence, e.Timestamp, e.Actor, e.Action, e.Subject, e.Detail);
                if (e.PrevHash != prev || e.Hash != expected) return new AuditVerification(false, db.AuditEntries.Count, e.Sequence);
                prev = e.Hash;
            }
            return new AuditVerification(true, db.AuditEntries.Count, null);
        }
    }
}
