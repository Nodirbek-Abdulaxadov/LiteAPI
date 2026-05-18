using System.Collections.Concurrent;

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5103");
var app = builder.Build();

var store = new ConcurrentDictionary<int, Todo>();
var nextId = 0;

app.MapGet("/healthz", () => "ok");

app.MapGet("/todos", () => store.Values.ToArray());

app.MapGet("/todos/{id:int}", (int id) =>
    store.TryGetValue(id, out var t) ? Results.Ok(t) : Results.NotFound());

app.MapPost("/todos", (CreateTodo dto) =>
{
    var id = Interlocked.Increment(ref nextId);
    var t = new Todo(id, dto.Title, false);
    store[id] = t;
    return Results.Ok(t);
});

app.MapPut("/todos/{id:int}", (int id, UpdateTodo dto) =>
{
    if (!store.ContainsKey(id)) return Results.NotFound();
    var updated = new Todo(id, dto.Title, dto.Done);
    store[id] = updated;
    return Results.Ok(updated);
});

app.MapDelete("/todos/{id:int}", (int id) =>
    store.TryRemove(id, out _) ? Results.NoContent() : Results.NotFound());

app.MapPost("/echo", (EchoDto dto) => Results.Ok(dto));

app.Run();

public record Todo(int Id, string Title, bool Done);
public record CreateTodo(string Title);
public record UpdateTodo(string Title, bool Done);
public record EchoDto(string Message);
