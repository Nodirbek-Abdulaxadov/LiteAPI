using System.Buffers;
using System.Runtime.InteropServices;

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
    private static int _started;  // 0 = idle, 1 = running

    public static unsafe void StartRustListener(LiteWebApplication app, LiteServerOptions options)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException("StartRustListener can only be called once per process.");

        // Pass limits to the Rust listener without changing the native ABI.
        Environment.SetEnvironmentVariable(
            "LITEAPI_RUST_MAX_CONCURRENT",
            options.MaxConcurrentRequests.ToString(CultureInfo.InvariantCulture));

        if (options.MaxRequestBodyBytes is long maxBody && maxBody > 0)
        {
            Environment.SetEnvironmentVariable(
                "LITEAPI_RUST_MAX_BODY_BYTES",
                maxBody.ToString(CultureInfo.InvariantCulture));
        }

        _handle = Handle;
        _free = Free;

        var handlePtr = Marshal.GetFunctionPointerForDelegate(_handle);
        var freePtr = Marshal.GetFunctionPointerForDelegate(_free);

        start_listener_v2(handlePtr, freePtr);

        IntPtr Handle(IntPtr methodPtr, IntPtr pathPtr, IntPtr headersPtr, IntPtr remoteIpPtr, byte* bodyPtr, nuint bodyLen, out nuint responseLen)
        {
            responseLen = 0;

            // UTF-8 decode preserves non-ASCII paths/headers; PtrToStringAnsi
            // silently corrupts anything outside 0x00-0x7F.
            var method = PtrToUtf8(methodPtr) ?? "GET";
            var rawPath = PtrToUtf8(pathPtr) ?? "/";
            var headersText = PtrToUtf8(headersPtr) ?? string.Empty;
            var remoteIp = PtrToUtf8(remoteIpPtr);

            var (path, query) = SplitPathAndQuery(rawPath);
            var headers = ParseHeaders(headersText);

            var contentType = headers.TryGetValue("Content-Type", out var ct) ? ct : null;

            var bodyLenInt = checked((int)bodyLen);
            var body = bodyLenInt == 0 ? Array.Empty<byte>() : new byte[bodyLenInt];
            if (bodyLenInt > 0)
                Marshal.Copy((IntPtr)bodyPtr, body, 0, bodyLenInt);

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

            // Snapshot headers so the framework's auto-added Content-Length /
            // Connection don't mutate user-visible state from a middleware.
            var bytes = BuildHttpResponseBytes(method, response, ctx.ResponseHeaders);

            // Hand back HGlobal-allocated memory; the matching Free runs after
            // Rust copies it into its own buffer.
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

    private static unsafe string? PtrToUtf8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        return Marshal.PtrToStringUTF8(ptr);
    }

    private static Dictionary<string, string> ParseHeaders(string headersText)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headersText.Length == 0) return dict;

        var span = headersText.AsSpan();
        while (span.Length > 0)
        {
            int nl = span.IndexOf('\n');
            ReadOnlySpan<char> line;
            if (nl < 0)
            {
                line = span;
                span = ReadOnlySpan<char>.Empty;
            }
            else
            {
                line = span[..nl];
                span = span[(nl + 1)..];
            }

            // Strip trailing CR.
            if (line.Length > 0 && line[^1] == '\r') line = line[..^1];
            if (line.Length == 0) continue;

            int colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (name.Length == 0) continue;

            dict[name.ToString()] = value.ToString();
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
        var parsed = HttpUtility.ParseQueryString(q);
        foreach (string? key in parsed.AllKeys!)
        {
            if (key != null)
                query[key] = parsed[key]!;
        }

        return (pathOnly, query);
    }

    /// <summary>
    /// Builds the wire bytes for the response. Headers are constructed without
    /// allocating a temporary string for the StringBuilder, and the auto-added
    /// Content-Length / Connection are written separately so the user's
    /// <see cref="LiteHttpContext.ResponseHeaders"/> dictionary is not mutated.
    /// </summary>
    private static byte[] BuildHttpResponseBytes(string method, Response response, Dictionary<string, string> responseHeaders)
    {
        var isHead = string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);

        byte[] body;
        if (isHead)
        {
            body = Array.Empty<byte>();
        }
        else if (response.IsStreaming)
        {
            // Rust ABI returns a single contiguous buffer; materialise the
            // stream into one. The managed host streams properly.
            using var ms = new MemoryStream();
            response.StreamWriter!(ms, CancellationToken.None).GetAwaiter().GetResult();
            body = ms.ToArray();
        }
        else
        {
            body = response.Body ?? Array.Empty<byte>();
        }

        var writer = new ArrayBufferWriter<byte>(initialCapacity: 256 + body.Length);

        // Status line.
        WriteAscii(writer, "HTTP/1.1 ");
        WriteAscii(writer, response.StatusCode.ToString(CultureInfo.InvariantCulture));
        WriteAscii(writer, " ");
        WriteAscii(writer, GetReasonPhrase(response.StatusCode));
        WriteAscii(writer, "\r\n");

        // User-supplied headers.
        var hasContentType = false;
        foreach (var h in responseHeaders)
        {
            if (string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) hasContentType = true;
            else if (string.Equals(h.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue; // framework owns this
            WriteHeader(writer, h.Key, h.Value);
        }

        // Auto-add Content-Type when the user didn't set one.
        if (!hasContentType && !string.IsNullOrWhiteSpace(response.ContentType))
            WriteHeader(writer, "Content-Type", response.ContentType);

        // Always set the framework's Content-Length so the client knows where
        // the body ends. Connection header is intentionally omitted — the Rust
        // listener decides keep-alive based on the *request*, and HTTP/1.1
        // clients default to keep-alive when no Connection header is sent.
        WriteHeader(writer, "Content-Length", body.Length.ToString(CultureInfo.InvariantCulture));

        WriteAscii(writer, "\r\n");

        if (body.Length > 0)
        {
            writer.Write(body);
        }

        return writer.WrittenSpan.ToArray();
    }

    private static void WriteHeader(IBufferWriter<byte> writer, string name, string value)
    {
        WriteAscii(writer, name);
        WriteAscii(writer, ": ");
        // Header values are written as UTF-8 to tolerate non-ASCII characters
        // (e.g. Uzbek strings in custom headers). Most HTTP/1.1 clients treat
        // header bytes as Latin-1; UTF-8 is a strict superset for the common
        // ASCII case and round-trips correctly when both ends decode UTF-8.
        var byteCount = Encoding.UTF8.GetByteCount(value);
        var span = writer.GetSpan(byteCount);
        var written = Encoding.UTF8.GetBytes(value, span);
        writer.Advance(written);
        WriteAscii(writer, "\r\n");
    }

    private static void WriteAscii(IBufferWriter<byte> writer, string ascii)
    {
        var span = writer.GetSpan(ascii.Length);
        for (int i = 0; i < ascii.Length; i++)
            span[i] = (byte)ascii[i];
        writer.Advance(ascii.Length);
    }

    private static string GetReasonPhrase(int statusCode) => statusCode switch
    {
        200 => "OK",
        201 => "Created",
        202 => "Accepted",
        204 => "No Content",
        301 => "Moved Permanently",
        302 => "Found",
        304 => "Not Modified",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        413 => "Payload Too Large",
        429 => "Too Many Requests",
        431 => "Request Header Fields Too Large",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        _   => "OK"
    };
}
