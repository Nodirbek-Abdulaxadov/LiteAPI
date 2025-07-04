using lite;
using LiteAPI;
using LiteAPI.Features.Configurations;
using System.Diagnostics;

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
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (ctx, next) =>
{
    var sw = Stopwatch.StartNew();
    await next();
    sw.Stop();
});

app.MapGroup<UsersRoutes>("/api/users");
app.Get("/config", request =>
{
    var config = request.GetService<LiteConfiguration>();
    return Response.OkJson(config);
}).RequireRoles("Admin");

app.Run();