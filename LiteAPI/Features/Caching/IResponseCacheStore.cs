/// <summary>
/// Swap-able backing store for cached responses. The middleware owns key
/// composition; the store only deals with opaque keys.
/// </summary>
public interface IResponseCacheStore
{
    ValueTask<CachedResponse?> TryGetAsync(string key, CancellationToken cancellationToken = default);
    ValueTask SetAsync(string key, CachedResponse entry, CancellationToken cancellationToken = default);
    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable snapshot of a cached response. The body is a copy — callers
/// must not mutate it after handing it to the store.
/// </summary>
public sealed record CachedResponse(
    int StatusCode,
    string ContentType,
    byte[] Body,
    DateTimeOffset ExpiresAt);
