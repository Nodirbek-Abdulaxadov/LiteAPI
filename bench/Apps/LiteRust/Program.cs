using System.Collections.Concurrent;

// Rust listener is configured via env (see LiteAPI/Interop/RustBridge.cs).
Environment.SetEnvironmentVariable("LITEAPI_RUST_ADDR", "127.0.0.1:5102");

var builder = LiteWebApplication.CreateBuilder(args);
var app = builder.Build();

var store = new ConcurrentDictionary<int, Todo>();
var nextId = 0;

app.Get("/healthz", () => Response.Ok("ok"));

app.Get("/todos", () => Response.OkJson(store.Values.ToArray()));

app.Get("/todos/{id}", ([FromRoute] int id) =>
    store.TryGetValue(id, out var t) ? Response.OkJson(t) : Response.NotFound());

app.Post("/todos", ([FromBody] CreateTodo dto) =>
{
    var id = Interlocked.Increment(ref nextId);
    var t = new Todo(id, dto.Title, false);
    store[id] = t;
    return Response.OkJson(t);
});

app.Put("/todos/{id}", ([FromRoute] int id, [FromBody] UpdateTodo dto) =>
{
    if (!store.ContainsKey(id)) return Response.NotFound();
    var updated = new Todo(id, dto.Title, dto.Done);
    store[id] = updated;
    return Response.OkJson(updated);
});

app.Delete("/todos/{id}", ([FromRoute] int id) =>
    store.TryRemove(id, out _) ? Response.NoContent() : Response.NotFound());

app.Post("/echo", ([FromBody] EchoDto dto) => Response.OkJson(dto));

app.RunWithRust(new LiteServerOptions { MaxConcurrentRequests = 1024 });

public record Todo(int Id, string Title, bool Done);
public record CreateTodo(string Title);
public record UpdateTodo(string Title, bool Done);
public record EchoDto(string Message);
