﻿namespace LiteAPI.Features.Cors;

public static class LiteWebApplicationCorsExtensions
{
    public static LiteCorsBuilder UseCors(this LiteWebApplication app, params string[] origins)
    {
        var builder = new LiteCorsBuilder();
        foreach (var origin in origins)
            builder.AllowedOrigins.Add(origin);

        app.Use(async (ctx, next) =>
        {
            var origin = ctx.Headers.TryGetValue("Origin", out var reqOrigin) ? reqOrigin : null;
            string? allowOrigin = null;

            if (builder.AllowAnyOriginFlag)
            {
                allowOrigin = "*";
            }
            else if (origin != null && builder.AllowedOrigins.Contains(origin))
            {
                allowOrigin = origin;
            }

            if (allowOrigin != null)
            {
                ctx.RawResponse.Headers["Access-Control-Allow-Origin"] = allowOrigin;
                ctx.RawResponse.Headers["Access-Control-Allow-Methods"] = builder.AllowedMethods;
                ctx.RawResponse.Headers["Access-Control-Allow-Headers"] = builder.AllowedHeaders;
                if (builder.AllowCredentialsFlag)
                    ctx.RawResponse.Headers["Access-Control-Allow-Credentials"] = "true";
            }

            if (ctx.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response = Response.NoContent();
                return;
            }

            await next();
        });

        return builder;
    }
}