using LiteAPI;

var builder = LiteWebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGroup<UsersRoutes>("/api/users");

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