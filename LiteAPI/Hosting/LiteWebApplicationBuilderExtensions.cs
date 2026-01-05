public static class LiteWebApplicationBuilderExtensions
{
    public static LiteWebApplicationBuilder Configure<TConfig>(this LiteWebApplicationBuilder builder)
        where TConfig : LiteConfiguration, new()
    {
        var instance = new TConfig();
        instance.Initialize();

        builder.LiteConfiguration = instance;

        // Clear previous registration if any:
        builder.Services.AddSingleton<LiteConfiguration>(instance);

        return builder;
    }

    public static LiteWebApplication UseAuthentication(this LiteWebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            var options = app.AuthOptions;
            if (options.DefaultScheme == AuthScheme.None)
            {
                await next();
                return;
            }

            if (ctx.RouteMetadata.AllowAnonymous)
            {
                await next();
                return;
            }

            bool isAuthenticated = false;

            if (options.DefaultScheme == AuthScheme.ApiKey &&
                ctx.Headers.TryGetValue(options.ApiKeyHeader, out var apiKey) &&
                options.ValidateApiKey?.Invoke(apiKey) == true)
            {
                isAuthenticated = true;
            }
            else if (options.DefaultScheme == AuthScheme.Bearer &&
                     ctx.Headers.TryGetValue("Authorization", out var authHeader) &&
                     authHeader.StartsWith("Bearer ") &&
                     options.ValidateBearerToken?.Invoke(authHeader["Bearer ".Length..].Trim()) == true)
            {
                isAuthenticated = true;
            }

            if (!isAuthenticated)
            {
                ctx.Response = Response.Unauthorized();
                return;
            }

            await next();
        });

        return app;
    }

    public static LiteWebApplication UseAuthorization(this LiteWebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            var metadata = ctx.RouteMetadata;

            if (metadata.AllowAnonymous)
            {
                await next();
                return;
            }

            if (!string.IsNullOrEmpty(metadata.RequiredPolicy))
            {
                var policy = app.AuthorizationOptions.GetPolicy(metadata.RequiredPolicy);
                if (policy is null || !policy(ctx))
                {
                    ctx.Response = Response.Forbid();
                    return;
                }
            }

            if (metadata.RequiredRoles.Count > 0)
            {
                if (!ctx.Headers.TryGetValue("X-Role", out var role) ||
                    !metadata.RequiredRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
                {
                    ctx.Response = Response.Forbid();
                    return;
                }
            }

            await next();
        });

        return app;
    }
}