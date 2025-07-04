namespace LiteAPI;

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

            foreach (var route in routes)
            {
                var normalizedPath = NormalizePath(route.Key.path);
                var method = route.Key.method.ToLower();

                if (!paths.ContainsKey(normalizedPath))
                    paths[normalizedPath] = new Dictionary<string, object>();

                var parameters = ExtractParameters(normalizedPath);

                paths[normalizedPath][method] = new
                {
                    summary = $"{method.ToUpper()} {normalizedPath}",
                    operationId = $"{method}_{normalizedPath.Replace("/", "_").Replace("{", "").Replace("}", "")}",
                    parameters = parameters.Any() ? parameters : null,
                    responses = new
                    {
                        _200 = new { description = "Success" }
                    }
                };
            }

            var openApi = new
            {
                openapi = "3.0.0",
                info = new { title, version },
                paths
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
}