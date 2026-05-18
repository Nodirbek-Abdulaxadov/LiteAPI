/// <summary>
/// <c>UseResponseCaching</c> middleware: caches successful (200) GET/HEAD
/// responses in a swap-able store and serves subsequent matches from cache
/// without invoking the rest of the pipeline.
/// </summary>
public static class ResponseCachingExtensions
{
    private const string CacheHeaderName = "X-Cache";
    private const string CacheHit = "HIT";
    private const string CacheMiss = "MISS";

    public static LiteWebApplication UseResponseCaching(
        this LiteWebApplication app,
        Action<ResponseCacheOptions>? configure = null,
        IResponseCacheStore? store = null)
    {
        var options = new ResponseCacheOptions();
        configure?.Invoke(options);

        if (options.TtlSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.TtlSeconds), "TtlSeconds must be > 0.");
        if (options.MaxCachedBodyBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxCachedBodyBytes), "MaxCachedBodyBytes must be > 0.");

        var backing = store ?? new InMemoryResponseCacheStore();
        var ttl = TimeSpan.FromSeconds(options.TtlSeconds);

        var varyHeaders = (options.VaryByHeaders ?? Array.Empty<string>())
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim())
            .ToArray();

        app.Use(async (ctx, next) =>
        {
            if (ctx.Method is not "GET" and not "HEAD") { await next(); return; }

            if (ctx.Headers.TryGetValue("Cache-Control", out var cc)
                && cc.Contains("no-store", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var key = BuildCacheKey(ctx, options, varyHeaders);

            var hit = await backing.TryGetAsync(key).ConfigureAwait(false);
            if (hit is not null)
            {
                ctx.SetResponseHeader(CacheHeaderName, CacheHit);
                ctx.Response = new Response
                {
                    StatusCode = hit.StatusCode,
                    ContentType = hit.ContentType,
                    Body = hit.Body.Length == 0 ? Array.Empty<byte>() : (byte[])hit.Body.Clone()
                };
                return;
            }

            await next();

            var response = ctx.Response;
            if (response is null) return;
            ctx.SetResponseHeader(CacheHeaderName, CacheMiss);

            // Streaming responses can't be cached without consuming them.
            if (response.IsStreaming) return;
            if (response.StatusCode != 200) return;
            if (response.Body is null) return;
            if (response.Body.LongLength > options.MaxCachedBodyBytes) return;

            var bodyCopy = response.Body.Length == 0
                ? Array.Empty<byte>()
                : (byte[])response.Body.Clone();

            await backing.SetAsync(key, new CachedResponse(
                response.StatusCode,
                response.ContentType,
                bodyCopy,
                DateTimeOffset.UtcNow.Add(ttl))).ConfigureAwait(false);
        });

        return app;
    }

    private static string BuildCacheKey(LiteHttpContext ctx, ResponseCacheOptions options, string[] varyHeaders)
    {
        var sb = new StringBuilder(64);
        sb.Append(ctx.Method);
        sb.Append('|');
        sb.Append(ctx.Path);

        if (options.IncludeQueryString && ctx.Query.Count > 0)
        {
            sb.Append("|q=");
            var first = true;
            foreach (var kv in ctx.Query.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (!first) sb.Append('&');
                sb.Append(kv.Key).Append('=').Append(kv.Value);
                first = false;
            }
        }

        foreach (var header in varyHeaders)
        {
            sb.Append("|h:").Append(header).Append('=');
            if (ctx.Headers.TryGetValue(header, out var hv))
                sb.Append(hv);
        }

        return sb.ToString();
    }
}
