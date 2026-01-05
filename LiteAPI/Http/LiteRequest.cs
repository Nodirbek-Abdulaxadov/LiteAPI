using System.Net;

namespace LiteAPI.Http;

public sealed class LiteRequest
{
    public string Method { get; }
    public string Path { get; }
    public Dictionary<string, string> Headers { get; }
    public Dictionary<string, string> Query { get; }
    public long ContentLength { get; }
    public string? ContentType { get; }
    public Stream BodyStream { get; }
    public string? RemoteIp { get; }

    /// <summary>
    /// Available only when running via HttpListener.
    /// </summary>
    public HttpListenerRequest? Raw { get; }

    internal LiteRequest(HttpListenerRequest rawRequest)
    {
        Raw = rawRequest;
        Method = rawRequest.HttpMethod;
        Path = rawRequest.Url?.AbsolutePath ?? "/";
        ContentLength = rawRequest.ContentLength64;
        ContentType = rawRequest.ContentType;
        BodyStream = rawRequest.InputStream;
        RemoteIp = rawRequest.RemoteEndPoint?.Address.ToString();

        Headers = rawRequest.Headers.AllKeys?
            .ToDictionary(k => k!, k => rawRequest.Headers[k!]!, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(rawRequest.Url?.Query))
        {
            var parsed = System.Web.HttpUtility.ParseQueryString(rawRequest.Url.Query);
            foreach (string key in parsed.AllKeys!)
            {
                if (key != null)
                    Query[key] = parsed[key]!;
            }
        }
    }

    internal LiteRequest(
        string method,
        string path,
        Dictionary<string, string>? headers,
        Dictionary<string, string>? query,
        Stream bodyStream,
        long contentLength,
        string? contentType,
        string? remoteIp)
    {
        Method = method;
        Path = path;
        Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Query = query ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        BodyStream = bodyStream;
        ContentLength = contentLength;
        ContentType = contentType;
        RemoteIp = remoteIp;
    }
}
