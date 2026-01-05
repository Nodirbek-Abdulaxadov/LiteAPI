using System.Diagnostics;
using System.Reflection;

public static class LiteObservabilityExtensions
{
    private static readonly LiteMetrics _metrics = new();

    /// <summary>
    /// Adds an X-Request-Id response header and assigns ctx.TraceId.
    /// If the client provides X-Request-Id, it will be used.
    /// </summary>
    public static LiteWebApplication UseRequestId(this LiteWebApplication app, string headerName = "X-Request-Id")
    {
        app.Use(async (ctx, next) =>
        {
            if (string.IsNullOrWhiteSpace(ctx.TraceId))
                ctx.TraceId = ctx.Headers.TryGetValue(headerName, out var incoming) && !string.IsNullOrWhiteSpace(incoming)
                    ? incoming
                    : Guid.NewGuid().ToString("n");

            ctx.SetResponseHeader(headerName, ctx.TraceId);
            await next();
        });

        return app;
    }

    /// <summary>
    /// Collects minimal in-memory metrics.
    /// </summary>
    public static LiteWebApplication UseMetrics(this LiteWebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            _metrics.OnRequestStart();
            try
            {
                await next();
            }
            finally
            {
                var status = ctx.Response?.StatusCode ?? 404;
                _metrics.OnRequestEnd(status);
            }
        });

        return app;
    }

    /// <summary>
    /// Maps GET /healthz returning status + version + metrics snapshot.
    /// </summary>
    public static LiteWebApplication MapHealthz(this LiteWebApplication app, string path = "/healthz")
    {
        app.Get(path, () =>
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = asm.GetName().Version?.ToString() ?? "unknown";

            var snapshot = _metrics.Snapshot();
            return Response.OkJson(new
            {
                status = "ok",
                version,
                uptimeSeconds = snapshot.UptimeSeconds,
                metrics = new
                {
                    snapshot.TotalRequests,
                    snapshot.ActiveRequests,
                    snapshot.Total4xx,
                    snapshot.Total5xx
                }
            });
        }).AllowAnonymous();

        return app;
    }

    public static LiteMetricsSnapshot GetMetricsSnapshot() => _metrics.Snapshot();
}
