using lite;

var builder = LiteWebApplication.CreateBuilder(args);

builder.Configure<Configurations>();
builder.AddAuthentication(options =>
{
    options.DefaultScheme = AuthScheme.Bearer;
    options.ValidateBearerToken = token => token == "secret-token";
});
builder.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", ctx =>
        ctx.Headers.TryGetValue("X-Role", out var role) && role == "Admin");
});

var app = builder.Build();

app.UseLogging();
app.UseCompression();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.UseOpenApi("/swagger", "LiteAPI Example", "1.0");
app.Get("/", ctx => Response.Ok("Welcome to LiteAPI 🚀"));
app.MapGroup<UsersRoutes>("/api/users");

app.Run();