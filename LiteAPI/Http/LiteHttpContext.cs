using System.Net;

namespace LiteAPI;

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

    internal LiteHttpContext(HttpListenerRequest request, Dictionary<string, string> routeParams)
    {
        RawRequest = request;
        Method = request.HttpMethod;
        Path = request.Url!.AbsolutePath;
        ContentLength = request.ContentLength64;
        ContentType = request.ContentType;
        Headers = request.Headers.AllKeys!.ToDictionary(k => k!, k => request.Headers[k]!, StringComparer.OrdinalIgnoreCase)!;
        Params = routeParams;

        Query = [];
        if (!string.IsNullOrEmpty(request.Url.Query))
        {
            foreach (var pair in request.Url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2)
                    Query[WebUtility.UrlDecode(kv[0])] = WebUtility.UrlDecode(kv[1]);
            }
        }
    }
}