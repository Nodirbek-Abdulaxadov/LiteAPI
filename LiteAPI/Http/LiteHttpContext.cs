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
    /// Response headers set by middlewares/features. For HttpListener mode these are copied to RawResponse.
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

        SetResponseHeader("X-Request-Id", TraceId);
    }

    internal LiteHttpContext(LiteRequest request, Dictionary<string, string>? routeParams = null)
    {
        Request = request;
        Params = routeParams ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        TraceId = Headers.TryGetValue("X-Request-Id", out var incoming) && !string.IsNullOrWhiteSpace(incoming)
            ? incoming
            : Guid.NewGuid().ToString("n");

        SetResponseHeader("X-Request-Id", TraceId);
    }

    public void SetResponseHeader(string name, string value)
    {
        ResponseHeaders[name] = value;
        if (RawResponse is not null)
            RawResponse.Headers[name] = value;
    }
}