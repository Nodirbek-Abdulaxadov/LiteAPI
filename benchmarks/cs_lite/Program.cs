using LiteAPI;

var builder = LiteWebApplication.CreateBuilder(args);
var app = builder.Build();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.Get("/weatherforecast", request =>
{
    var forecast = Enumerable.Range(1, 500).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return Response.OkJson(forecast);
});

app.Get("/api/users", request =>
{
    var query = request.GetFromQuery<QueryParams>();
    return Response.OkJson(new { query.Page, query.PageSize, query.Search });
});

app.Post("/api/users", request =>
{
    var user = request.GetFromBody<UserDto>();
    return Response.OkJson(user);
});

app.Post("/api/users/form", request =>
{
    var user = request.GetFromForm<UserDto>();
    return Response.OkJson(user);
});


app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}


internal class QueryParams
    {
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Search { get; set; }
}

internal class UserDto
{
    public override string ToString() => $"{Name} ({Email}), Age: {Age}";

    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Age { get; set; } = 0;
}