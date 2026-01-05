/// <summary>
/// Extension methods for LiteWebApplication, including route grouping and static file serving.
/// </summary>
public static class LiteWebApplicationExtensions
{
    /// <summary>
    /// Enables grouping endpoints under a common prefix for clean organization.
    /// </summary>
    public static void MapGroup(this LiteWebApplication app, string prefix, Action<LiteWebApplicationGroup> configure)
    {
        var group = new LiteWebApplicationGroup(app.Router, prefix);
        configure(group);
    }

    public static void MapGroup<TGroup>(this LiteWebApplication app, string prefix)
        where TGroup : ILiteGroup, new()
    {
        var group = new LiteWebApplicationGroup(app.Router, prefix, app.Services);
        var instance = new TGroup();
        instance.Register(group);
    }

    /// <summary>
    /// Enables zero-dependency static file serving from the specified root folder (default: wwwroot).
    /// Uses "/{*path}" fallback routing to catch and serve unregistered file routes.
    /// </summary>
    public static void MapStaticFiles(this LiteWebApplication app, string root = "wwwroot")
    {
        app.Get("/{*path}", (LiteAPI.Http.LiteRequest request) =>
        {
            var requestedPath = request.Path.Trim('/');

            requestedPath = Uri.UnescapeDataString(requestedPath);

            // Serve index.html for "/"
            if (string.IsNullOrEmpty(requestedPath))
                requestedPath = "index.html";

            return ServeStaticFile(root, requestedPath);
        });
    }

    /// <summary>
    /// Reads a file from disk and returns it as a Response with correct Content-Type.
    /// Returns 404 if the file does not exist.
    /// </summary>
    private static Response ServeStaticFile(string root, string requestedPath)
    {
        var rootFullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), root));
        var safeRelativePath = requestedPath.Replace('/', Path.DirectorySeparatorChar);
        var candidateFullPath = Path.GetFullPath(Path.Combine(rootFullPath, safeRelativePath));

        if (!candidateFullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
            return Response.NotFound();

        if (!File.Exists(candidateFullPath))
            return Response.NotFound();

        var contentType = Path.GetExtension(candidateFullPath).ToLowerInvariant() switch
        {
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream"
        };

        var fileBytes = File.ReadAllBytes(candidateFullPath);

        return new Response
        {
            StatusCode = 200,
            ContentType = contentType,
            Body = fileBytes
        };
    }
}