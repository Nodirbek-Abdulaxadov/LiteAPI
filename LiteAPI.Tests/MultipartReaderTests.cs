using System.Text;
using LiteAPI.Http;
using Xunit;

public class MultipartReaderTests
{
    private const string Boundary = "----TestBoundary123";

    private static byte[] BuildBody(params (string headers, byte[] body)[] parts)
    {
        var ms = new MemoryStream();
        foreach (var (headers, body) in parts)
        {
            Write(ms, "--" + Boundary + "\r\n");
            Write(ms, headers + "\r\n\r\n");
            ms.Write(body, 0, body.Length);
            Write(ms, "\r\n");
        }
        Write(ms, "--" + Boundary + "--\r\n");
        return ms.ToArray();

        static void Write(Stream s, string text) { var b = Encoding.UTF8.GetBytes(text); s.Write(b, 0, b.Length); }
    }

    [Fact]
    public void Parses_text_field()
    {
        var body = BuildBody(
            ("Content-Disposition: form-data; name=\"username\"", Encoding.UTF8.GetBytes("najim")));

        var parts = MultipartReader.Parse(body, $"multipart/form-data; boundary={Boundary}");

        Assert.Single(parts);
        Assert.Equal("username", parts[0].Name);
        Assert.False(parts[0].IsFile);
        Assert.Equal("najim", parts[0].AsString());
    }

    [Fact]
    public void Parses_file_field_with_binary_body_intact()
    {
        // Binary payload with embedded zero bytes and CRLFs to ensure the
        // parser doesn't choke on them.
        var binary = new byte[] { 0x00, 0x01, 0x0D, 0x0A, 0xFF, 0xFE, 0x42 };
        var body = BuildBody(
            ("Content-Disposition: form-data; name=\"avatar\"; filename=\"pic.bin\"\r\nContent-Type: application/octet-stream",
             binary));

        var parts = MultipartReader.Parse(body, $"multipart/form-data; boundary={Boundary}");

        Assert.Single(parts);
        Assert.Equal("avatar", parts[0].Name);
        Assert.Equal("pic.bin", parts[0].FileName);
        Assert.Equal("application/octet-stream", parts[0].ContentType);
        Assert.Equal(binary, parts[0].Data);
        Assert.True(parts[0].IsFile);
    }

    [Fact]
    public void Parses_multiple_mixed_parts_in_order()
    {
        var body = BuildBody(
            ("Content-Disposition: form-data; name=\"a\"", Encoding.UTF8.GetBytes("1")),
            ("Content-Disposition: form-data; name=\"b\"", Encoding.UTF8.GetBytes("2")),
            ("Content-Disposition: form-data; name=\"file\"; filename=\"f.txt\"\r\nContent-Type: text/plain",
             Encoding.UTF8.GetBytes("hello")));

        var parts = MultipartReader.Parse(body, $"multipart/form-data; boundary={Boundary}");

        Assert.Equal(3, parts.Count);
        Assert.Equal("a", parts[0].Name);
        Assert.Equal("1", parts[0].AsString());
        Assert.Equal("b", parts[1].Name);
        Assert.Equal("file", parts[2].Name);
        Assert.True(parts[2].IsFile);
        Assert.Equal("hello", parts[2].AsString());
    }

    [Fact]
    public void Missing_boundary_returns_empty()
    {
        Assert.Empty(MultipartReader.Parse("not-multipart"u8, "text/plain"));
    }

    [Fact]
    public void Quoted_boundary_is_unquoted_correctly()
    {
        var body = BuildBody(
            ("Content-Disposition: form-data; name=\"a\"", Encoding.UTF8.GetBytes("v")));

        var parts = MultipartReader.Parse(body, $"multipart/form-data; boundary=\"{Boundary}\"");

        Assert.Single(parts);
        Assert.Equal("v", parts[0].AsString());
    }
}
