using System.Runtime.InteropServices;
using System.Text;

internal static class RustBridge
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate IntPtr HandleRequestV2Delegate(
        IntPtr method,
        IntPtr path,
        IntPtr headers,
        IntPtr remoteIp,
        byte* bodyPtr,
        nuint bodyLen,
        out nuint responseLen);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FreeBytesDelegate(IntPtr ptr, nuint len);

    [DllImport("liteapi_rust", EntryPoint = "start_listener_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int start_listener_v2(IntPtr handleCb, IntPtr freeCb);

    // Keep delegates alive for the duration of the listener.
    private static HandleRequestV2Delegate? _handle;
    private static FreeBytesDelegate? _free;

    public static unsafe void StartRustListener(LiteWebApplication app, LiteServerOptions options)
    {
        // Pass limits to the Rust listener without changing the native ABI.
        Environment.SetEnvironmentVariable(
            "LITEAPI_RUST_MAX_CONCURRENT",
            options.MaxConcurrentRequests.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (options.MaxRequestBodyBytes is long maxBody && maxBody > 0)
        {
            Environment.SetEnvironmentVariable(
                "LITEAPI_RUST_MAX_BODY_BYTES",
                maxBody.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        _handle = Handle;
        _free = Free;

        var handlePtr = Marshal.GetFunctionPointerForDelegate(_handle);
        var freePtr = Marshal.GetFunctionPointerForDelegate(_free);

        start_listener_v2(handlePtr, freePtr);

        IntPtr Handle(IntPtr methodPtr, IntPtr pathPtr, IntPtr headersPtr, IntPtr remoteIpPtr, byte* bodyPtr, nuint bodyLen, out nuint responseLen)
        {
            responseLen = 0;

            var method = Marshal.PtrToStringAnsi(methodPtr) ?? "GET";
            var rawPath = Marshal.PtrToStringAnsi(pathPtr) ?? "/";
            var headersText = Marshal.PtrToStringAnsi(headersPtr) ?? string.Empty;
            var remoteIp = Marshal.PtrToStringAnsi(remoteIpPtr);

            var (path, query) = SplitPathAndQuery(rawPath);
            var headers = ParseHeaders(headersText);

            var contentType = headers.TryGetValue("Content-Type", out var ct) ? ct : null;

            var body = new byte[(int)bodyLen];
            if (bodyLen > 0)
                Marshal.Copy((IntPtr)bodyPtr, body, 0, (int)bodyLen);

            var req = new LiteAPI.Http.LiteRequest(
                method,
                path,
                headers,
                query,
                new MemoryStream(body, writable: false),
                body.Length,
                contentType,
                remoteIp);

            app.Router.TryResolve(method.ToUpperInvariant(), path, out var matchedRoute, out var routeParams);

            var ctx = new LiteHttpContext(req, routeParams);
            ctx.RouteMetadata = matchedRoute?.Metadata ?? new RouteMetadata { AllowAnonymous = true };

            Response response;
            try
            {
                response = app.ProcessRequestAsync(ctx, matchedRoute, routeParams, options)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                response = Response.InternalServerError(ex.Message);
            }

            var bytes = BuildHttpResponseBytes(method, response, ctx.ResponseHeaders);
            var ptr = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            responseLen = (nuint)bytes.Length;
            return ptr;
        }

        static void Free(IntPtr ptr, nuint _)
        {
            if (ptr != IntPtr.Zero)
                Marshal.FreeHGlobal(ptr);
        }
    }

    private static Dictionary<string, string> ParseHeaders(string headersText)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in headersText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimEnd('\r');
            var idx = trimmed.IndexOf(':');
            if (idx <= 0)
                continue;

            var name = trimmed[..idx].Trim();
            var value = trimmed[(idx + 1)..].Trim();
            if (name.Length == 0)
                continue;

            dict[name] = value;
        }

        return dict;
    }

    private static (string Path, Dictionary<string, string> Query) SplitPathAndQuery(string rawPath)
    {
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var qIndex = rawPath.IndexOf('?', StringComparison.Ordinal);
        if (qIndex < 0)
            return (rawPath, query);

        var pathOnly = rawPath[..qIndex];
        var q = rawPath[(qIndex + 1)..];
        var parsed = System.Web.HttpUtility.ParseQueryString(q);
        foreach (string key in parsed.AllKeys!)
        {
            if (key != null)
                query[key] = parsed[key]!;
        }

        return (pathOnly, query);
    }

    private static byte[] BuildHttpResponseBytes(string method, Response response, Dictionary<string, string> responseHeaders)
    {
        var isHead = string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);
        var body = isHead ? Array.Empty<byte>() : (response.Body ?? Array.Empty<byte>());

        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ");
        sb.Append(response.StatusCode);
        sb.Append(' ');
        sb.Append(GetReasonPhrase(response.StatusCode));
        sb.Append("\r\n");

        // Ensure Content-Type/Length are present.
        var hasContentType = responseHeaders.ContainsKey("Content-Type");
        if (!hasContentType && !string.IsNullOrWhiteSpace(response.ContentType))
            responseHeaders["Content-Type"] = response.ContentType;

        responseHeaders["Content-Length"] = body.Length.ToString();
        responseHeaders["Connection"] = "close";

        foreach (var h in responseHeaders)
        {
            sb.Append(h.Key);
            sb.Append(": ");
            sb.Append(h.Value);
            sb.Append("\r\n");
        }

        sb.Append("\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());

        if (body.Length == 0)
            return headerBytes;

        var result = new byte[headerBytes.Length + body.Length];
        Buffer.BlockCopy(headerBytes, 0, result, 0, headerBytes.Length);
        Buffer.BlockCopy(body, 0, result, headerBytes.Length, body.Length);
        return result;
    }

    private static string GetReasonPhrase(int statusCode) => statusCode switch
    {
        200 => "OK",
        201 => "Created",
        202 => "Accepted",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        413 => "Payload Too Large",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        _ => "OK"
    };
}
