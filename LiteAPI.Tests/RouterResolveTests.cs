using Xunit;

public class RouterResolveTests
{
    [Fact]
    public void TryResolve_Prefers_Static_Over_Param_And_Wildcard()
    {
        var router = new Router();

        router.Get("/a/{id}", () => Response.Ok("param"));
        router.Get("/a/static", () => Response.Ok("static"));
        router.Get("/{*path}", () => Response.Ok("wild"));

        var ok = router.TryResolve("GET", "/a/static", out var route, out var routeParams);

        Assert.True(ok);
        Assert.NotNull(route);
        Assert.Equal("/a/static", route!.Path);
        Assert.Empty(routeParams);
    }

    [Fact]
    public void TryResolve_Wildcard_Captures_Remainder()
    {
        var router = new Router();
        router.Get("/files/{*path}", () => Response.Ok("ok"));

        var ok = router.TryResolve("GET", "/files/a/b/c.txt", out var route, out var routeParams);

        Assert.True(ok);
        Assert.NotNull(route);
        Assert.Equal("/files/{*path}", route!.Path);
        Assert.True(routeParams.TryGetValue("path", out var captured));
        Assert.Equal("a/b/c.txt", captured);
    }

    [Fact]
    public void SetMetadata_Also_Updates_RouteDefinition_Metadata_When_Route_Exists()
    {
        var router = new Router();
        var def = router.Get("/secure", () => Response.Ok("ok"));

        router.SetMetadata("GET", "/secure", meta => meta.RequiredRoles.Add("Admin"));

        Assert.Contains("Admin", def.Metadata.RequiredRoles);
    }
}
