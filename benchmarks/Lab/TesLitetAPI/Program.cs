var builder = LiteWebApplication.CreateBuilder(args);

var app = builder.Build();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild",
    "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.Get("/weatherforecast", req =>
{
    var forecast = Enumerable.Range(1, 50).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 35),
            summaries[Random.Shared.Next(summaries.Length)]
        )
    ).ToArray();

    return Response.OkJson(forecast);
});

app.RunWithRust();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}