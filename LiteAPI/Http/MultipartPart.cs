namespace LiteAPI.Http;

/// <summary>
/// One section of a <c>multipart/form-data</c> body as parsed by
/// <see cref="MultipartReader"/>. Holds both metadata (name, file name,
/// content type, raw headers) and the binary part body.
/// </summary>
public sealed class MultipartPart
{
    /// <summary>Form field name from <c>Content-Disposition</c>.</summary>
    public string Name { get; }

    /// <summary>File name from <c>Content-Disposition</c>, or <c>null</c> for non-file parts.</summary>
    public string? FileName { get; }

    /// <summary>Per-part Content-Type, or <c>null</c> when not sent.</summary>
    public string? ContentType { get; }

    /// <summary>Raw part body (unbuffered text fields included).</summary>
    public byte[] Data { get; }

    /// <summary>All headers that appeared inside the part block.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    public bool IsFile => FileName is not null;

    public MultipartPart(string name, string? fileName, string? contentType, byte[] data, IReadOnlyDictionary<string, string> headers)
    {
        Name = name;
        FileName = fileName;
        ContentType = contentType;
        Data = data;
        Headers = headers;
    }

    /// <summary>Decode the body as UTF-8 (override with <paramref name="encoding"/>).</summary>
    public string AsString(Encoding? encoding = null)
        => (encoding ?? Encoding.UTF8).GetString(Data);

    /// <summary>Wrap the body in a non-owning <see cref="MemoryStream"/>.</summary>
    public Stream AsStream() => new MemoryStream(Data, writable: false);
}
