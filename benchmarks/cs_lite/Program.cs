using lite;
using LiteAPI;
using LiteAPI.Features.Configurations;
using System.Diagnostics;

var builder = LiteWebApplication.CreateBuilder(args);
builder.Configure<Configurations>();

var app = builder.Build();
app.UseLogging();

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
});

app.UseOpenApi("/swagger", "LiteAPI Example", "v1");

app.Run();

internal class QueryParams
    {
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Search { get; set; }
}
internal class UserDto
{
    public int Id { get; set; } = 0;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Age { get; set; } = 0;
    public override string ToString() => $"{Name} ({Email}), Age: {Age}";

}