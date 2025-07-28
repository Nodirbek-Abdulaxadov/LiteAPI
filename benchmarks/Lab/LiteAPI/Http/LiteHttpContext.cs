public class LiteHttpContext
{
    public string Method { get; }
    public string Path { get; }
    public Dictionary<string, string> Headers { get; }
    public Dictionary<string, string> Query { get; }
    public Dictionary<string, string> Params { get; }
    public long ContentLength { get; }
    public string? ContentType { get; }
    public HttpListenerRequest RawRequest { get; }
    public HttpListenerResponse RawResponse { get; }
    public Response? Response { get; set; }
    public RouteMetadata RouteMetadata { get; set; } = new();

    public LiteHttpContext(HttpListenerContext context, Dictionary<string, string>? routeParams = null)
    {
        RawRequest = context.Request;
        RawResponse = context.Response;

        Path = RawRequest.Url?.AbsolutePath ?? "/";
        Method = RawRequest.HttpMethod;
        ContentLength = RawRequest.ContentLength64;
        ContentType = RawRequest.ContentType;

        Headers = RawRequest.Headers.AllKeys?
            .ToDictionary(k => k!, k => RawRequest.Headers[k!]!, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Params = routeParams ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(RawRequest.Url?.Query))
        {
            var parsed = HttpUtility.ParseQueryString(RawRequest.Url.Query);
            foreach (string key in parsed.AllKeys!)
            {
                if (key != null)
                    Query[key] = parsed[key]!;
            }
        }
    }
}