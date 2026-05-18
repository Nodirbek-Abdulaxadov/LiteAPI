using Xunit;

// HandleRawRequest covers the binding pipeline (route params, query, JSON body)
// without needing to spin up an HttpListener.

public class RouterBindingTests
{
    public record Item(int Id, string Name);

    [Fact]
    public void Route_param_int_is_parsed()
    {
        var router = new Router();
        router.Get("/items/{id}", ([FromRoute] int id) => Response.OkJson(new { id }));

        var resp = router.HandleRawRequest("GET", "/items/42", body: null);
        Assert.Equal(200, resp.StatusCode);
        Assert.Contains("42", resp.GetBodyAsString());
    }

    [Fact]
    public void Invalid_route_param_returns_400_not_500()
    {
        var router = new Router();
        router.Get("/items/{id}", ([FromRoute] int id) => Response.OkJson(new { id }));

        var resp = router.HandleRawRequest("GET", "/items/not-a-number", body: null);
        Assert.Equal(400, resp.StatusCode);
    }

    [Fact]
    public void Guid_route_param_is_parsed()
    {
        var router = new Router();
        router.Get("/items/{id}", ([FromRoute] Guid id) => Response.OkJson(new { id }));

        var g = Guid.NewGuid();
        var resp = router.HandleRawRequest("GET", $"/items/{g}", body: null);
        Assert.Equal(200, resp.StatusCode);
        Assert.Contains(g.ToString(), resp.GetBodyAsString());
    }

    [Fact]
    public void Query_param_is_parsed()
    {
        var router = new Router();
        router.Get("/search", ([FromQuery] string q) => Response.Ok(q));

        var resp = router.HandleRawRequest("GET", "/search?q=hello", body: null);
        Assert.Equal(200, resp.StatusCode);
        Assert.Equal("hello", resp.GetBodyAsString());
    }

    [Fact]
    public void Body_is_deserialized_for_POST_complex_param()
    {
        var router = new Router();
        router.Post("/items", (Item item) => Response.OkJson(item));

        var resp = router.HandleRawRequest("POST", "/items", "{\"id\":7,\"name\":\"widget\"}");
        Assert.Equal(200, resp.StatusCode);
        Assert.Contains("widget", resp.GetBodyAsString());
    }

    [Fact]
    public void FromBody_is_deserialized()
    {
        var router = new Router();
        router.Post("/echo", ([FromBody] Item item) => Response.OkJson(item));

        var resp = router.HandleRawRequest("POST", "/echo", "{\"id\":3,\"name\":\"x\"}");
        Assert.Equal(200, resp.StatusCode);
        Assert.Contains("\"id\":3", resp.GetBodyAsString());
    }

    [Fact]
    public void Invalid_JSON_body_returns_400()
    {
        var router = new Router();
        router.Post("/items", ([FromBody] Item item) => Response.OkJson(item));

        var resp = router.HandleRawRequest("POST", "/items", "{ not valid json");
        Assert.Equal(400, resp.StatusCode);
    }

    [Fact]
    public void Multiple_body_params_return_400()
    {
        var router = new Router();
        router.Post("/two", ([FromBody] Item a, [FromBody] Item b) => Response.OkJson(new { a, b }));

        var resp = router.HandleRawRequest("POST", "/two", "{\"id\":1,\"name\":\"a\"}");
        Assert.Equal(400, resp.StatusCode);
    }

    [Fact]
    public async Task Task_Response_handler_is_awaited()
    {
        var router = new Router();
        router.Get("/async", async () =>
        {
            await Task.Delay(5);
            return Response.Ok("done");
        });

        // HandleRawRequest blocks under the covers but the underlying
        // path is fully async — make sure the result is correct.
        var resp = await Task.Run(() => router.HandleRawRequest("GET", "/async", null));
        Assert.Equal(200, resp.StatusCode);
        Assert.Equal("done", resp.GetBodyAsString());
    }
}
