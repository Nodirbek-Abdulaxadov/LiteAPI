public static class LiteWebApplicationMiddlewareExtensions
{
    public static LiteWebApplication UseLogging(this LiteWebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"[{ctx.TraceId}] {ctx.Method} {ctx.Path}");
            await next();
            sw.Stop();
            Console.WriteLine($"[{ctx.TraceId}] {ctx.Method} {ctx.Path} -> {ctx.Response?.StatusCode} in {sw.ElapsedMilliseconds}ms");
        });
        return app;
    }

    public static LiteWebApplication UseCors(this LiteWebApplication app, string allowOrigin = "*")
    {
        app.Use(async (ctx, next) =>
        {
            ctx.RawResponse.Headers["Access-Control-Allow-Origin"] = allowOrigin;
            ctx.RawResponse.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
            ctx.RawResponse.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";

            if (ctx.Method == "OPTIONS")
            {
                ctx.Response = Response.NoContent();
            }
            else
            {
                await next();
            }
        });
        return app;
    }

    public static LiteWebApplication UsePoweredBy(this LiteWebApplication app, string poweredBy = "LiteAPI")
    {
        app.Use(async (ctx, next) =>
        {
            ctx.RawResponse.Headers["X-Powered-By"] = poweredBy;
            await next();
        });
        return app;
    }
}