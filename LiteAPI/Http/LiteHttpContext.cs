using LiteAPI.Http;

public class LiteHttpContext
{
    public string TraceId { get; set; } = string.Empty;

    public string Method => Request.Method;
    public string Path => Request.Path;

    public Dictionary<string, string> Headers => Request.Headers;
    public Dictionary<string, string> Query => Request.Query;
    public Dictionary<string, string> Params { get; }
    public long ContentLength => Request.ContentLength;
    public string? ContentType => Request.ContentType;

    public LiteRequest Request { get; }

    /// <summary>
    /// Response headers set by middlewares / features.
    /// In <see cref="HttpListener"/> mode these are flushed to the underlying
    /// <see cref="HttpListenerResponse"/> at write time — do not also write to
    /// <c>RawResponse.Headers</c> in user code, the host does it for you.
    /// </summary>
    public Dictionary<string, string> ResponseHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HttpListenerRequest? RawRequest => Request.Raw;
    public HttpListenerResponse? RawResponse { get; }

    public Response? Response { get; set; }
    public RouteMetadata RouteMetadata { get; set; } = new();

    public string? RemoteIp => Request.RemoteIp;

    public LiteHttpContext(HttpListenerContext context, Dictionary<string, string>? routeParams = null)
    {
        RawResponse = context.Response;
        Request = new LiteRequest(context.Request);

        Params = routeParams ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        TraceId = Headers.TryGetValue("X-Request-Id", out var incoming) && !string.IsNullOrWhiteSpace(incoming)
            ? incoming
            : Guid.NewGuid().ToString("n");

        ResponseHeaders["X-Request-Id"] = TraceId;
    }

    internal LiteHttpContext(LiteRequest request, Dictionary<string, string>? routeParams = null)
    {
        Request = request;
        Params = routeParams ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        TraceId = Headers.TryGetValue("X-Request-Id", out var incoming) && !string.IsNullOrWhiteSpace(incoming)
            ? incoming
            : Guid.NewGuid().ToString("n");

        ResponseHeaders["X-Request-Id"] = TraceId;
    }

    /// <summary>
    /// Sets a response header. Header writes are buffered until the host
    /// flushes them onto the underlying response — calling this twice with
    /// the same name overwrites the previous value, never duplicates.
    /// </summary>
    public void SetResponseHeader(string name, string value)
        => ResponseHeaders[name] = value;
}
