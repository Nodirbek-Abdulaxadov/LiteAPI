public static class LiteCompressionMiddlewareExtensions
{
    public static LiteWebApplication UseCompression(this LiteWebApplication app, int minBytes = 1024)
    {
        app.Use(async (ctx, next) =>
        {
            if (ctx.Headers.TryGetValue("Accept-Encoding", out var encoding) &&
                encoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
            {
                await next();

                var response = ctx.Response;
                if (response != null && response.Body.Length >= minBytes)
                {
                    // Avoid double compression
                    if (ctx.ResponseHeaders.TryGetValue("Content-Encoding", out var existing) && !string.IsNullOrWhiteSpace(existing))
                        return;

                    // Only compress "compressible" content types
                    var contentType = response.ContentType ?? "";
                    if (!IsCompressibleContentType(contentType))
                        return;

                    using var output = new MemoryStream();
                    using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
                    {
                        gzip.Write(response.Body, 0, response.Body.Length);
                    }

                    ctx.SetResponseHeader("Content-Encoding", "gzip");
                    ctx.SetResponseHeader("Content-Type", response.ContentType ?? "application/octet-stream");

                    ctx.Response = new Response
                    {
                        StatusCode = response.StatusCode,
                        ContentType = response.ContentType ?? "application/octet-stream",
                        Body = output.ToArray()
                    };
                }
            }
            else
            {
                await next();
            }
        });

        return app;
    }

    private static bool IsCompressibleContentType(string contentType)
    {
        // Strip charset, etc.
        var semi = contentType.IndexOf(';');
        if (semi >= 0)
            contentType = contentType[..semi];
        contentType = contentType.Trim().ToLowerInvariant();

        if (contentType.StartsWith("text/", StringComparison.Ordinal))
            return true;

        return contentType is "application/json"
            or "application/javascript"
            or "application/xml"
            or "application/xhtml+xml"
            or "image/svg+xml";
    }
}