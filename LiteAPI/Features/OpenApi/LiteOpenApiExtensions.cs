public static class LiteOpenApiExtensions
{
    public static LiteWebApplication UseOpenApi(
        this LiteWebApplication app,
        string path = "/swagger",
        string title = "LiteAPI",
        string version = "1.0")
    {
        app.Get(path, ctx =>
        {
            var routes = app.Router.GetRoutes();

            var paths = new Dictionary<string, Dictionary<string, object>>();

            object? components = null;
            if (app.AuthOptions.DefaultScheme != AuthScheme.None)
            {
                components = new
                {
                    securitySchemes = new Dictionary<string, object>
                    {
                        ["Bearer"] = new
                        {
                            type = "http",
                            scheme = "bearer",
                            bearerFormat = "JWT"
                        },
                        ["ApiKey"] = new
                        {
                            type = "apiKey",
                            @in = "header",
                            name = app.AuthOptions.ApiKeyHeader
                        }
                    }
                };
            }

            foreach (var route in routes)
            {
                var normalizedPath = NormalizePath(route.Key.path);
                var method = route.Key.method.ToLower();

                if (!paths.ContainsKey(normalizedPath))
                    paths[normalizedPath] = new Dictionary<string, object>();

                var def = route.Value;
                var parameters = ExtractParameters(normalizedPath);
                var extraParams = ExtractQueryParameters(def);
                if (extraParams.Count > 0)
                    parameters.AddRange(extraParams);

                var requiresAuth = app.AuthOptions.DefaultScheme != AuthScheme.None && !def.Metadata.AllowAnonymous;
                object? security = null;
                if (requiresAuth)
                {
                    security = app.AuthOptions.DefaultScheme switch
                    {
                        AuthScheme.Bearer => new[] { new Dictionary<string, string[]>() { ["Bearer"] = Array.Empty<string>() } },
                        AuthScheme.ApiKey => new[] { new Dictionary<string, string[]>() { ["ApiKey"] = Array.Empty<string>() } },
                        _ => null
                    };
                }

                var requestBody = ExtractRequestBody(def);

                paths[normalizedPath][method] = new
                {
                    summary = $"{method.ToUpper()} {normalizedPath}",
                    operationId = $"{method}_{normalizedPath.Replace("/", "_").Replace("{", "").Replace("}", "")}",
                    parameters = parameters.Any() ? parameters : null,
                    requestBody,
                    security,
                    responses = BuildResponses(requiresAuth)
                };
            }

            var openApi = new
            {
                openapi = "3.0.0",
                info = new { title, version },
                paths,
                components
            };

            return Response.Json(openApi);
        });

        app.Get($"{path}/ui", ctx =>
        {
            var html = $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <title>LiteAPI Swagger UI</title>
                <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist/swagger-ui.css" />
            </head>
            <body>
                <div id="swagger-ui"></div>
                <script src="https://unpkg.com/swagger-ui-dist/swagger-ui-bundle.js"></script>
                <script>
                    SwaggerUIBundle({
                        url: '{{path}}',
                        dom_id: '#swagger-ui'
                    });
                </script>
            </body>
            </html>
            """;
            return Response.Html(html);
        });

        return app;
    }

    private static string NormalizePath(string path)
    {
        // Normalize ":id" to "{id}" for OpenAPI
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].StartsWith(":"))
                segments[i] = "{" + segments[i][1..] + "}";
        }
        return "/" + string.Join("/", segments);
    }

    private static List<object> ExtractParameters(string path)
    {
        var parameters = new List<object>();
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (segment.StartsWith("{") && segment.EndsWith("}"))
            {
                var name = segment[1..^1];
                parameters.Add(new
                {
                    name,
                    @in = "path",
                    required = true,
                    schema = new { type = "string" }
                });
            }
        }

        return parameters;
    }

    private static List<object> ExtractQueryParameters(RouteDefinition def)
    {
        var result = new List<object>();
        var parameters = def.Handler.Method.GetParameters();

        foreach (var p in parameters)
        {
            if (p.GetCustomAttributes(typeof(FromQueryAttribute), inherit: true).FirstOrDefault() is null)
                continue;

            var isSimple = p.ParameterType.IsPrimitive
                           || p.ParameterType.IsEnum
                           || p.ParameterType == typeof(string)
                           || p.ParameterType == typeof(Guid)
                           || p.ParameterType == typeof(DateTime)
                           || p.ParameterType == typeof(decimal);

            if (!isSimple)
                continue;

            result.Add(new
            {
                name = p.Name,
                @in = "query",
                required = false,
                schema = new { type = "string" }
            });
        }

        return result;
    }

    private static object? ExtractRequestBody(RouteDefinition def)
    {
        var method = def.Method;
        if (method is not ("POST" or "PUT" or "PATCH"))
            return null;

        var parameters = def.Handler.Method.GetParameters();
        foreach (var p in parameters)
        {
            if (p.GetCustomAttributes(typeof(FromBodyAttribute), inherit: true).Any())
            {
                return new
                {
                    required = true,
                    content = new Dictionary<string, object>
                    {
                        ["application/json"] = new { schema = new { type = "object" } }
                    }
                };
            }

            if (p.GetCustomAttributes(typeof(FromFormAttribute), inherit: true).Any())
            {
                return new
                {
                    required = true,
                    content = new Dictionary<string, object>
                    {
                        ["application/x-www-form-urlencoded"] = new { schema = new { type = "object" } },
                        ["multipart/form-data"] = new { schema = new { type = "object" } }
                    }
                };
            }
        }

        return null;
    }

    private static object BuildResponses(bool requiresAuth)
    {
        var baseResponses = new Dictionary<string, object>
        {
            ["200"] = new { description = "Success" },
            ["400"] = new { description = "Bad Request" },
            ["404"] = new { description = "Not Found" },
            ["500"] = new { description = "Internal Server Error" }
        };

        if (requiresAuth)
        {
            baseResponses["401"] = new { description = "Unauthorized" };
            baseResponses["403"] = new { description = "Forbidden" };
        }

        return baseResponses;
    }
}