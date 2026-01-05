var builder = LiteWebApplication.CreateBuilder(args);
var app = builder.Build();

app.Get("/", () => Response.Ok("Hello from LiteAPI.Example"));
app.Get("/ping", () => Response.OkJson(new { status = "ok", time = DateTime.UtcNow }));

app.RunWithRust();