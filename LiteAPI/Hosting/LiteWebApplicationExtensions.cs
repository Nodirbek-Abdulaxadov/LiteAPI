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
    /// Enables zero-dependency static file serving from the specified root folder
    /// (default <c>wwwroot</c>). Uses a low-priority <c>"/{*path}"</c> fallback
    /// so any explicit route the application has already registered wins.
    /// Large files are streamed straight from disk to the response — no full
    /// allocation into memory.
    /// </summary>
    public static void MapStaticFiles(this LiteWebApplication app, string root = "wwwroot")
    {
        // TryHandle skips registration if the user already owns "/{*path}".
        var registered = app.Router.TryHandle("GET", "/{*path}", async (LiteAPI.Http.LiteRequest request) =>
        {
            var requestedPath = Uri.UnescapeDataString(request.Path.Trim('/'));
            if (string.IsNullOrEmpty(requestedPath))
                requestedPath = "index.html";

            return await ServeStaticFileAsync(root, requestedPath);
        });

        if (registered is null)
            Console.WriteLine("MapStaticFiles: '/{*path}' is already registered — static-file fallback skipped.");
    }

    private static async Task<Response> ServeStaticFileAsync(string root, string requestedPath)
    {
        var rootFullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), root));
        var safeRelativePath = requestedPath.Replace('/', Path.DirectorySeparatorChar);
        var candidateFullPath = Path.GetFullPath(Path.Combine(rootFullPath, safeRelativePath));

        // Reject path traversal: candidate must live under the configured root.
        if (!candidateFullPath.StartsWith(rootFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidateFullPath, rootFullPath, StringComparison.OrdinalIgnoreCase))
            return Response.NotFound();

        if (!File.Exists(candidateFullPath))
            return Response.NotFound();

        var contentType = Path.GetExtension(candidateFullPath).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css"            => "text/css; charset=utf-8",
            ".js" or ".mjs"   => "application/javascript; charset=utf-8",
            ".json"           => "application/json; charset=utf-8",
            ".png"            => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif"            => "image/gif",
            ".webp"           => "image/webp",
            ".svg"            => "image/svg+xml",
            ".ico"            => "image/x-icon",
            ".txt"            => "text/plain; charset=utf-8",
            ".pdf"            => "application/pdf",
            ".woff"           => "font/woff",
            ".woff2"          => "font/woff2",
            ".wasm"           => "application/wasm",
            _                 => "application/octet-stream"
        };

        // Stream straight off disk — avoids a synchronous full-file allocation.
        await using var fs = new FileStream(candidateFullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        using var ms = new MemoryStream();
        await fs.CopyToAsync(ms);
        return Response.Bytes(ms.ToArray(), contentType);
    }
}
