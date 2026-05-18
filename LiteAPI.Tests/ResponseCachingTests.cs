using System.Text;
using LiteAPI.Http;
using Xunit;

// These tests exercise the caching middleware against a minimal pipeline
// that mimics LiteWebApplication.ProcessRequestAsync. We can't easily spin
// up an HttpListener inside a unit test, so we drive the middleware delegate
// directly through a fake LiteWebApplication built via the public builder.

public class ResponseCachingTests
{
    private sealed class TestApp
    {
        public LiteWebApplication App { get; }
        public InMemoryResponseCacheStore Store { get; }

        public TestApp(Action<ResponseCacheOptions>? configure = null)
        {
            var builder = LiteWebApplication.CreateBuilder([]);
            App = builder.Build();
            Store = new InMemoryResponseCacheStore();
            App.UseResponseCaching(configure, Store);
        }

        public Task<Response> RunAsync(string method, string path,
            Dictionary<string, string>? headers = null,
            Dictionary<string, string>? query = null,
            Func<LiteHttpContext, Response>? terminal = null)
        {
            var request = new LiteRequest(
                method: method,
                path: path,
                headers: headers,
                query: query,
                bodyStream: new MemoryStream(Array.Empty<byte>(), writable: false),
                contentLength: 0,
                contentType: null,
                remoteIp: null);

            var ctx = new LiteHttpContext(request);

            // Register the terminal handler on the router so ProcessRequestAsync
            // can dispatch to it like a real request.
            var def = App.Get(path, () => terminal!(ctx));
            return App.ProcessRequestAsync(ctx, def, ctx.Params, new LiteServerOptions());
        }
    }

    [Fact]
    public async Task Cache_hit_returns_same_body_without_reinvoking_handler()
    {
        var app = new TestApp();
        var calls = 0;

        var first = await app.RunAsync("GET", "/items", terminal: _ => Response.Ok($"v{++calls}"));
        var second = await app.RunAsync("GET", "/items", terminal: _ => Response.Ok($"v{++calls}"));

        Assert.Equal(1, calls);
        Assert.Equal(first.GetBodyAsString(), second.GetBodyAsString());
    }

    [Fact]
    public async Task POST_is_never_cached()
    {
        var app = new TestApp();
        var calls = 0;

        await app.RunAsync("POST", "/items", terminal: _ => Response.Ok($"v{++calls}"));
        await app.RunAsync("POST", "/items", terminal: _ => Response.Ok($"v{++calls}"));

        Assert.Equal(2, calls);
        Assert.Equal(0, app.Store.Count);
    }

    [Fact]
    public async Task Non_200_responses_are_never_cached()
    {
        var app = new TestApp();
        var calls = 0;

        await app.RunAsync("GET", "/missing", terminal: _ => { calls++; return Response.NotFound("nope"); });
        await app.RunAsync("GET", "/missing", terminal: _ => { calls++; return Response.NotFound("nope"); });

        Assert.Equal(2, calls);
        Assert.Equal(0, app.Store.Count);
    }

    [Fact]
    public async Task Cache_control_no_store_bypasses_cache()
    {
        var app = new TestApp();
        var calls = 0;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Cache-Control"] = "no-store" };

        await app.RunAsync("GET", "/items", headers: headers, terminal: _ => Response.Ok($"v{++calls}"));
        await app.RunAsync("GET", "/items", headers: headers, terminal: _ => Response.Ok($"v{++calls}"));

        Assert.Equal(2, calls);
        Assert.Equal(0, app.Store.Count);
    }

    [Fact]
    public async Task Streaming_responses_are_not_cached()
    {
        var app = new TestApp();
        var calls = 0;

        Response Streamed(LiteHttpContext _)
        {
            calls++;
            return Response.Stream(async (s, c) => await s.WriteAsync("hi"u8.ToArray()), "text/plain");
        }

        await app.RunAsync("GET", "/sse", terminal: Streamed);
        await app.RunAsync("GET", "/sse", terminal: Streamed);

        Assert.Equal(2, calls);
        Assert.Equal(0, app.Store.Count);
    }
}
