using Xunit;

public class StreamingResponseTests
{
    [Fact]
    public async Task Stream_helper_marks_response_as_streaming_and_writes_via_callback()
    {
        var resp = Response.Stream(async (output, ct) =>
        {
            await output.WriteAsync("hello"u8.ToArray());
            await output.WriteAsync(" world"u8.ToArray());
        }, "text/plain");

        Assert.True(resp.IsStreaming);

        using var ms = new MemoryStream();
        await resp.StreamWriter!(ms, CancellationToken.None);
        Assert.Equal("hello world", System.Text.Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task File_helper_streams_disk_file_bytes()
    {
        var temp = Path.GetTempFileName();
        await File.WriteAllTextAsync(temp, "from-disk");
        try
        {
            var resp = Response.File(temp, "text/plain");
            Assert.True(resp.IsStreaming);

            using var ms = new MemoryStream();
            await resp.StreamWriter!(ms, CancellationToken.None);
            Assert.Equal("from-disk", System.Text.Encoding.UTF8.GetString(ms.ToArray()));
        }
        finally { File.Delete(temp); }
    }

    [Fact]
    public void File_helper_returns_404_when_path_missing()
    {
        var resp = Response.File("Z:/definitely-not-here.bin");
        Assert.Equal(404, resp.StatusCode);
        Assert.False(resp.IsStreaming);
    }

    [Fact]
    public async Task Sse_helper_writes_data_frames_separated_by_blank_lines()
    {
        async IAsyncEnumerable<string> Events()
        {
            yield return "first";
            yield return "second";
            await Task.CompletedTask;
        }

        var resp = Response.Sse(Events());
        Assert.Equal("text/event-stream", resp.ContentType);

        using var ms = new MemoryStream();
        await resp.StreamWriter!(ms, CancellationToken.None);
        var text = System.Text.Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n");

        Assert.Contains("data: first\n\n", text);
        Assert.Contains("data: second\n\n", text);
    }

    [Fact]
    public void Buffered_response_is_not_marked_streaming()
    {
        var resp = Response.OkJson(new { hello = "world" });
        Assert.False(resp.IsStreaming);
        Assert.Null(resp.StreamWriter);
    }
}
