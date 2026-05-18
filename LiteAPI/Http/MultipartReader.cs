namespace LiteAPI.Http;

/// <summary>
/// RFC 7578 multipart/form-data reader. Operates on the in-memory body — the
/// caller is responsible for honouring whatever request-size limit is in force
/// before handing the bytes to <see cref="Parse(ReadOnlySpan{byte}, string)"/>.
/// </summary>
public static class MultipartReader
{
    private static readonly byte[] CRLF = [(byte)'\r', (byte)'\n'];

    /// <summary>
    /// Reads the entire stream then parses. Suitable for typical form posts;
    /// avoid for very large uploads — read the stream directly in that case.
    /// </summary>
    public static IReadOnlyList<MultipartPart> Parse(Stream body, string contentType)
    {
        using var ms = new MemoryStream();
        body.CopyTo(ms);
        return Parse(ms.GetBuffer().AsSpan(0, (int)ms.Length), contentType);
    }

    public static IReadOnlyList<MultipartPart> Parse(ReadOnlySpan<byte> body, string contentType)
    {
        var boundary = ExtractBoundary(contentType);
        if (boundary is null) return Array.Empty<MultipartPart>();

        var delim = "--" + boundary;
        var delimBytes = Encoding.ASCII.GetBytes(delim);

        var parts = new List<MultipartPart>();
        var pos = 0;

        // Skip the preamble up to the first boundary.
        var first = IndexOf(body, delimBytes, 0);
        if (first < 0) return parts;
        pos = first + delimBytes.Length;

        while (pos < body.Length)
        {
            // Close marker "--" right after the boundary -> end of stream.
            if (pos + 1 < body.Length && body[pos] == (byte)'-' && body[pos + 1] == (byte)'-')
                break;

            // Skip the CRLF that follows the boundary line.
            if (pos + 1 < body.Length && body[pos] == (byte)'\r' && body[pos + 1] == (byte)'\n')
                pos += 2;
            else if (pos < body.Length && body[pos] == (byte)'\n')
                pos += 1;

            // Locate the end of this part (next boundary occurrence).
            var nextBoundary = IndexOf(body, delimBytes, pos);
            if (nextBoundary < 0) break;

            // Headers block ends at the first CRLFCRLF inside [pos, nextBoundary).
            var headersEnd = IndexOf(body[pos..nextBoundary], "\r\n\r\n"u8, 0);
            if (headersEnd < 0) { pos = nextBoundary + delimBytes.Length; continue; }
            headersEnd += pos;

            var headersSlice = body[pos..headersEnd];
            var headers = ParseHeaders(headersSlice);

            var bodyStart = headersEnd + 4;                // skip CRLFCRLF
            var bodyEnd = nextBoundary;
            // Strip the trailing CRLF before the next boundary.
            if (bodyEnd >= bodyStart + 2 && body[bodyEnd - 2] == (byte)'\r' && body[bodyEnd - 1] == (byte)'\n')
                bodyEnd -= 2;
            else if (bodyEnd > bodyStart && body[bodyEnd - 1] == (byte)'\n')
                bodyEnd -= 1;

            var data = body[bodyStart..bodyEnd].ToArray();

            if (headers.TryGetValue("Content-Disposition", out var disposition))
            {
                var name = ParseDispositionField(disposition, "name");
                var fileName = ParseDispositionField(disposition, "filename");
                if (!string.IsNullOrEmpty(name))
                {
                    var contentTypeHeader = headers.TryGetValue("Content-Type", out var ct) ? ct : null;
                    parts.Add(new MultipartPart(name!, fileName, contentTypeHeader, data, headers));
                }
            }

            pos = nextBoundary + delimBytes.Length;
        }

        return parts;
    }

    private static string? ExtractBoundary(string contentType)
    {
        const string token = "boundary=";
        var idx = contentType.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var rest = contentType[(idx + token.Length)..].Trim();
        var semi = rest.IndexOf(';');
        if (semi >= 0) rest = rest[..semi].Trim();
        return rest.Trim('"');
    }

    private static Dictionary<string, string> ParseHeaders(ReadOnlySpan<byte> headerBlock)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = Encoding.UTF8.GetString(headerBlock);
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var colon = trimmed.IndexOf(':');
            if (colon <= 0) continue;
            var name = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim();
            if (name.Length == 0) continue;
            dict[name] = value;
        }
        return dict;
    }

    private static string? ParseDispositionField(string disposition, string field)
    {
        // Disposition looks like: form-data; name="foo"; filename="bar.txt"
        var token = field + "=";
        var idx = disposition.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var start = idx + token.Length;
        if (start < disposition.Length && disposition[start] == '"')
        {
            start++;
            var end = disposition.IndexOf('"', start);
            return end < 0 ? null : disposition[start..end];
        }

        var semi = disposition.IndexOf(';', start);
        return semi < 0 ? disposition[start..].Trim() : disposition[start..semi].Trim();
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, int start)
    {
        for (int i = start; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}
