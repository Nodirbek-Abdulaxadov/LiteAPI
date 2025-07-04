using System.IO.Compression;

namespace LiteAPI;

public static class LiteCompressionMiddlewareExtensions
{
    public static LiteWebApplication UseCompression(this LiteWebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            if (ctx.Headers.TryGetValue("Accept-Encoding", out var encoding) &&
                encoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
            {
                await next();

                var response = ctx.Response;
                if (response != null && response.Body.Length > 0)
                {
                    using var output = new MemoryStream();
                    using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
                    {
                        gzip.Write(response.Body, 0, response.Body.Length);
                    }

                    ctx.RawResponse.Headers["Content-Encoding"] = "gzip";
                    ctx.RawResponse.Headers["Content-Type"] = response.ContentType;
                    ctx.RawResponse.Headers["Content-Length"] = output.Length.ToString();

                    ctx.Response = new Response
                    {
                        StatusCode = response.StatusCode,
                        ContentType = response.ContentType,
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
}