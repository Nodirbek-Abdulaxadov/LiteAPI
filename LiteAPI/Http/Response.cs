public class Response
{
    public int StatusCode { get; set; } = 200;
    public string ContentType { get; set; } = "text/plain";

    /// <summary>
    /// Buffered response body. Ignored when <see cref="StreamWriter"/> is set.
    /// </summary>
    public byte[] Body { get; set; } = [];

    /// <summary>
    /// Optional writer used by the host to stream the response body directly
    /// to the underlying network stream. When set, <see cref="Body"/> is ignored
    /// and no <c>Content-Length</c> is sent — the host writes <c>chunked</c>-style
    /// (managed mode buffers, Rust mode currently buffers via a memory stream).
    /// </summary>
    public Func<Stream, CancellationToken, Task>? StreamWriter { get; set; }

    public bool IsStreaming => StreamWriter is not null;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string GetBodyAsString()
        => Body is { Length: > 0 } ? Encoding.UTF8.GetString(Body) : string.Empty;

    private static byte[] Encode(string text) => Encoding.UTF8.GetBytes(text);
    private static byte[] EncodeJson(object obj) => JsonSerializer.SerializeToUtf8Bytes(obj, _jsonOptions);

    public static Response Html(string html, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "text/html; charset=utf-8",
        Body = Encode(html)
    };

    public static Response Ok(string text) => new()
    {
        StatusCode = 200,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(text)
    };

    public static Response OkJson(object obj) => new()
    {
        StatusCode = 200,
        ContentType = "application/json; charset=utf-8",
        Body = EncodeJson(obj)
    };

    public static Response BadRequest(string message = "Bad Request") => new()
    {
        StatusCode = 400,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response NotFound(string message = "Not Found") => new()
    {
        StatusCode = 404,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response NoContent() => new()
    {
        StatusCode = 204,
        ContentType = "text/plain; charset=utf-8",
        Body = []
    };

    public static Response Created(string location, object? obj = null) => new()
    {
        StatusCode = 201,
        ContentType = "application/json; charset=utf-8",
        Body = obj is not null ? EncodeJson(obj) : []
    };

    public static Response Accepted(string location, object? obj = null) => new()
    {
        StatusCode = 202,
        ContentType = "application/json; charset=utf-8",
        Body = obj is not null ? EncodeJson(obj) : []
    };

    public static Response Conflict(string message = "Conflict") => new()
    {
        StatusCode = 409,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response Unauthorized(string message = "Unauthorized") => new()
    {
        StatusCode = 401,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response Forbid(string message = "Forbidden") => new()
    {
        StatusCode = 403,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response TooManyRequests(string message = "Too Many Requests") => new()
    {
        StatusCode = 429,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response PayloadTooLarge(string message = "Payload Too Large") => new()
    {
        StatusCode = 413,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response InternalServerError(string message = "Internal Server Error") => new()
    {
        StatusCode = 500,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response Text(string text, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(text)
    };

    public static Response Json(object obj, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "application/json; charset=utf-8",
        Body = EncodeJson(obj)
    };

    public static Response Bytes(byte[] body, string contentType, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = contentType,
        Body = body
    };

    // ── Streaming helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Streams the response body via the supplied writer. No <c>Content-Length</c>
    /// is set; the host flushes incremental writes through to the network stream.
    /// </summary>
    public static Response Stream(Func<Stream, CancellationToken, Task> writer, string contentType, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = contentType,
        StreamWriter = writer
    };

    /// <summary>
    /// Streams a file from disk. Content-Type is inferred from extension if not provided.
    /// Returns 404 if the file does not exist.
    /// </summary>
    public static Response File(string path, string? contentType = null)
    {
        if (!System.IO.File.Exists(path))
            return NotFound();

        var ct = contentType ?? InferContentType(path);
        return new Response
        {
            StatusCode = 200,
            ContentType = ct,
            StreamWriter = async (output, ct2) =>
            {
                await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
                await fs.CopyToAsync(output, 81920, ct2).ConfigureAwait(false);
            }
        };
    }

    /// <summary>
    /// Server-Sent Events helper. Each yielded string becomes one <c>data:</c>
    /// frame. Sets Content-Type to <c>text/event-stream</c>.
    /// </summary>
    public static Response Sse(IAsyncEnumerable<string> events)
    {
        return new Response
        {
            StatusCode = 200,
            ContentType = "text/event-stream",
            StreamWriter = async (output, ct) =>
            {
                using var writer = new StreamWriter(output, Encoding.UTF8, leaveOpen: true) { NewLine = "\n", AutoFlush = true };
                await foreach (var evt in events.WithCancellation(ct).ConfigureAwait(false))
                {
                    foreach (var line in evt.Split('\n'))
                        await writer.WriteLineAsync("data: " + line).ConfigureAwait(false);
                    await writer.WriteLineAsync().ConfigureAwait(false);
                }
            }
        };
    }

    private static string InferContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
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
}
