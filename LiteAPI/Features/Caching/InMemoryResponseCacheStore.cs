/// <summary>
/// Default in-process store. Uses a <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// and performs TTL eviction lazily on access — no background timer.
/// </summary>
public sealed class InMemoryResponseCacheStore : IResponseCacheStore
{
    private readonly ConcurrentDictionary<string, CachedResponse> _entries = new(StringComparer.Ordinal);

    public ValueTask<CachedResponse?> TryGetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(key, out var entry))
            return new ValueTask<CachedResponse?>((CachedResponse?)null);

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _entries.TryRemove(new KeyValuePair<string, CachedResponse>(key, entry));
            return new ValueTask<CachedResponse?>((CachedResponse?)null);
        }

        return new ValueTask<CachedResponse?>(entry);
    }

    public ValueTask SetAsync(string key, CachedResponse entry, CancellationToken cancellationToken = default)
    {
        _entries[key] = entry;
        return default;
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove(key, out _);
        return default;
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        _entries.Clear();
        return default;
    }

    /// <summary>Diagnostics hook for tests; never call from the request hot path.</summary>
    internal int Count => _entries.Count;
}
