namespace LiteAPI;

public class LiteWebApplicationBuilder
{
    public Router Router { get; } = new();
    public ServiceCollection Services { get; } = new();
    public string[] Urls { get; set; } = ["http://localhost:8080/"];

    public LiteWebApplication Build() => new(Router, Services, Urls);
}