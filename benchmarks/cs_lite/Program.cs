using LiteAPI;
using System.Net;

var builder = LiteWebApplication.CreateBuilder(args);
var app = builder.Build();

List<UserDto> users =
[
    new UserDto { Id = 1, Name = "Alice1", Email = "test1@gmail.com", Age = 31 },
    new UserDto { Id = 2, Name = "Alice2", Email = "test2@gmail.com", Age = 32 },
    new UserDto { Id = 3, Name = "Alice3", Email = "test3@gmail.com", Age = 33 },
];

app.Get("/api/users", (HttpListenerRequest req, [FromQuery] QueryParams query) =>
{
    return Response.OkJson(users);
});

app.Get("/api/users/{id}", (HttpListenerRequest req, [FromRoute] int id) =>
{
    var user = users.FirstOrDefault(u => u.Id == id);
    if (user == null)
        return Response.NotFound($"User with ID {id} not found.");
    return Response.OkJson(user);
});

app.Post("/api/users", (HttpListenerRequest req, [FromForm] UserDto newUser) =>
{
    if (newUser == null || string.IsNullOrWhiteSpace(newUser.Name) || string.IsNullOrWhiteSpace(newUser.Email))
        return Response.BadRequest("Invalid user data.");
    newUser.Id = users.Max(u => u.Id) + 1;
    users.Add(newUser);
    return Response.Created($"/api/users/{newUser.Id}", newUser);
});

app.Put("/api/users/{id}", (HttpListenerRequest req, int id, UserDto updatedUser) =>
{
    var user = users.FirstOrDefault(u => u.Id == id);
    if (user == null)
        return Response.NotFound($"User with ID {id} not found.");
    user.Name = updatedUser.Name;
    user.Email = updatedUser.Email;
    user.Age = updatedUser.Age;
    return Response.OkJson(user);
});

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