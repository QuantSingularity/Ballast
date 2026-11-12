namespace Ballast.Api.Services;

/// <summary>
/// Tracks failed sign-ins with bounded memory.
///
/// Two counters are kept for every attempt, whether or not the account exists, so the response never reveals which
/// emails are registered:
///  - per (account, client): five failures lock that client out of that account for five minutes. A stranger who
///    guesses a victim's email therefore only locks out their own address, not the victim.
///  - per account across all clients: a much higher ceiling that stops a distributed guessing run.
///
/// Failure counts decay after <see cref="FailureWindow"/> of quiet, and the store never holds more than the configured
/// number of entries: expired entries are swept first, then the oldest unlocked entries are dropped.
/// </summary>
public sealed class LoginThrottle(TimeProvider clock, int maxEntries = 10_000)
{
    public const int MaxFailuresPerClient = 5;
    public const int MaxFailuresPerAccount = 25;
    public const int MaxKeyLength = 254;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset LastFailure;
        public DateTimeOffset? LockedUntil;
    }

    private readonly Dictionary<string, Entry> _entries = [];
    private readonly object _lock = new();

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    private static string Normalize(string email)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        return e.Length > MaxKeyLength ? e[..MaxKeyLength] : e;
    }

    private static string ClientKey(string email, string client) => $"c|{email}|{client}";

    private static string AccountKey(string email) => $"a|{email}";

    public bool IsLocked(string email, string client)
    {
        email = Normalize(email);
        var now = clock.GetUtcNow();
        lock (_lock)
            return IsLockedLocked(ClientKey(email, client), now) || IsLockedLocked(AccountKey(email), now);
    }

    private bool IsLockedLocked(string key, DateTimeOffset now) =>
        _entries.TryGetValue(key, out var e) && e.LockedUntil is { } until && until > now;

    public void RecordFailure(string email, string client)
    {
        email = Normalize(email);
        var now = clock.GetUtcNow();
        lock (_lock)
        {
            Bump(ClientKey(email, client), MaxFailuresPerClient, now);
            Bump(AccountKey(email), MaxFailuresPerAccount, now);
            if (_entries.Count > maxEntries) Evict(now);
        }
    }

    public void RecordSuccess(string email, string client)
    {
        email = Normalize(email);
        lock (_lock)
        {
            _entries.Remove(ClientKey(email, client));
            _entries.Remove(AccountKey(email));
        }
    }

    private void Bump(string key, int limit, DateTimeOffset now)
    {
        if (!_entries.TryGetValue(key, out var e))
        {
            e = new Entry();
            _entries[key] = e;
        }
        bool lockExpired = e.LockedUntil is { } until && until <= now;
        if (lockExpired || now - e.LastFailure > FailureWindow)
        {
            e.Failures = 0;
            e.LockedUntil = null;
        }
        e.Failures++;
        e.LastFailure = now;
        if (e.Failures >= limit) e.LockedUntil = now.Add(LockDuration);
    }

    private void Evict(DateTimeOffset now)
    {
        foreach (var key in _entries.Where(kv => IsStale(kv.Value, now)).Select(kv => kv.Key).ToList())
            _entries.Remove(key);
        if (_entries.Count <= maxEntries) return;

        // Still over the cap: drop a tenth of the capacity, oldest unlocked entries first, locked ones only as a last resort.
        int toDrop = _entries.Count - maxEntries + Math.Max(1, maxEntries / 10);
        var victims = _entries
            .OrderBy(kv => kv.Value.LockedUntil is { } u && u > now ? 1 : 0)
            .ThenBy(kv => kv.Value.LastFailure)
            .Take(toDrop)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in victims) _entries.Remove(key);
    }

    private static bool IsStale(Entry e, DateTimeOffset now) =>
        (e.LockedUntil is null || e.LockedUntil <= now) && now - e.LastFailure > FailureWindow;
}
