var builder = LiteWebApplication.CreateBuilder(args);

builder.AddAuthentication(auth =>
{
	auth.DefaultScheme = AuthScheme.Bearer;
	auth.ValidateBearerToken = token => token == "secret-token";
});

builder.AddAuthorization(authz =>
{
	authz.AddPolicy("AdminOnly", ctx =>
		ctx.Headers.TryGetValue("X-Role", out var role) && role == "Admin");
});

var app = builder.Build();

app.UseLogging();
app.UseRequestId();
app.UseMetrics();
app.MapHealthz();
app.UseRateLimiting(maxRequests: 20, perSeconds: 10, perIp: true);
app.UseCompression(minBytes: 512);
app.UseAuthentication();
app.UseAuthorization();

app.Get("/", () => Response.Ok("Hello from LiteAPI.Example"))
	.AllowAnonymous();

app.Get("/ping", () => Response.OkJson(new { status = "ok", time = DateTime.UtcNow }))
	.AllowAnonymous();

app.Get("/secure", () => Response.Ok("You are authorized (role=Admin)"))
	.RequireRoles("Admin");

app.Post("/echo", ([FromBody] EchoDto dto) => Response.OkJson(dto));

app.Get("/large", () =>
{
	var payload = new string('a', 10_000);
	return Response.Text(payload);
}).AllowAnonymous();

app.Get("/policy", () => Response.Ok("Policy AdminOnly passed"))
	.RequirePolicy("AdminOnly");

app.MapStaticFiles();

app.Run(new LiteServerOptions
{
	MaxConcurrentRequests = 128,
	MaxRequestBodyBytes = 64 * 1024
});

public record EchoDto(string Message);