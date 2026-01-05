public static class LiteRateLimitingMiddlewareExtensions
{
    public static LiteWebApplication UseRateLimiting(
        this LiteWebApplication app,
        int maxRequests,
        int perSeconds = 60,
        bool perIp = true)
    {
        var window = TimeSpan.FromSeconds(perSeconds);
        var requests = new ConcurrentDictionary<string, (int Count, DateTime WindowStart)>();
        long lastCleanupTicks = DateTime.UtcNow.Ticks;
        var cleanupInterval = window;

        app.Use(async (ctx, next) =>
        {
            var key = perIp ? ctx.RawRequest.RemoteEndPoint?.Address.ToString() ?? "unknown" : "global";
            var now = DateTime.UtcNow;

            // Periodic cleanup to avoid unbounded memory growth.
            var last = new DateTime(Interlocked.Read(ref lastCleanupTicks), DateTimeKind.Utc);
            if (now - last > cleanupInterval)
            {
                if (Interlocked.CompareExchange(ref lastCleanupTicks, now.Ticks, last.Ticks) == last.Ticks)
                {
                    foreach (var kvp in requests)
                    {
                        if (now - kvp.Value.WindowStart > window)
                            requests.TryRemove(kvp.Key, out _);
                    }
                }
            }

            var entry = requests.GetOrAdd(key, _ => (0, now));

            if (now - entry.WindowStart > window)
            {
                entry = (0, now);
            }

            if (entry.Count >= maxRequests)
            {
                ctx.Response = Response.TooManyRequests("Rate limit exceeded. Please try again later.");
                return;
            }

            requests[key] = (entry.Count + 1, entry.WindowStart);
            await next();
        });

        return app;
    }
}