/// <summary>
/// Options for <c>UseResponseCaching</c>. All values can be overridden per-app.
/// </summary>
public sealed class ResponseCacheOptions
{
    /// <summary>Cache lifetime in seconds. Default 60.</summary>
    public int TtlSeconds { get; set; } = 60;

    /// <summary>If true, the query string participates in the cache key. Default true.</summary>
    public bool IncludeQueryString { get; set; } = true;

    /// <summary>Request headers whose values participate in the cache key (case-insensitive names).</summary>
    public string[] VaryByHeaders { get; set; } = Array.Empty<string>();

    /// <summary>Response bodies larger than this are never cached. Default 1 MB.</summary>
    public long MaxCachedBodyBytes { get; set; } = 1L * 1024 * 1024;
}
