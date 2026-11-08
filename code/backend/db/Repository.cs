using System.Collections.Concurrent;
using Ballast.Core;

namespace Ballast.Data;

public sealed class Repository<T>(string typeName, Func<T, string> key) where T : class
{
    private readonly ConcurrentDictionary<string, T> _items = new();

    public IReadOnlyList<T> All() => _items.Values.OrderBy(key, StringComparer.Ordinal).ToList();

    public T? Find(string id) => _items.TryGetValue(id, out var v) ? v : null;

    public T Get(string id) => Find(id) ?? throw new NotFoundException($"{typeName} '{id}' was not found");

    public void Upsert(T item) => _items[key(item)] = item;

    public bool Remove(string id) => _items.TryRemove(id, out _);

    public void ReplaceAll(IEnumerable<T> items)
    {
        _items.Clear();
        foreach (var i in items) Upsert(i);
    }

    public int Count => _items.Count;
}
