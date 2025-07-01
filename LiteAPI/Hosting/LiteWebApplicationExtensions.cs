namespace LiteAPI;

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

    /// <summary>
    /// Enables zero-dependency static file serving from the specified root folder (default: wwwroot).
    /// Uses "/{*path}" fallback routing to catch and serve unregistered file routes.
    /// </summary>
    public static void MapStaticFiles(this LiteWebApplication app, string root = "wwwroot")
    {
        app.Get("/{*path}", request =>
        {
            var requestedPath = request.Url!.AbsolutePath.Trim('/');

            // Serve index.html for "/"
            if (string.IsNullOrEmpty(requestedPath))
                requestedPath = "index.html";

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), root, requestedPath);
            return ServeStaticFile(filePath);
        });
    }

    /// <summary>
    /// Reads a file from disk and returns it as a Response with correct Content-Type.
    /// Returns 404 if the file does not exist.
    /// </summary>
    private static Response ServeStaticFile(string filePath)
    {
        var parts = filePath.Split(Path.DirectorySeparatorChar);
        //recombine the parts to ensure correct path handling
        filePath = string.Join('/', parts);

        if (!File.Exists(filePath))
            return Response.NotFound();

        var contentType = Path.GetExtension(filePath).ToLowerInvariant() switch
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

        var fileBytes = File.ReadAllBytes(filePath);

        return new Response
        {
            StatusCode = 200,
            ContentType = contentType,
            Body = fileBytes
        };
    }
}