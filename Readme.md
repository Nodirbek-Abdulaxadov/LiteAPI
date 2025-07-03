Here is your **fully updated, clean, professional `README.md`** for **LiteAPI**, reflecting **all current features without the outdated folder structure**:

---

# 🚀 LiteAPI

A **minimal, dependency-free C# micro web framework** for building **lightweight REST APIs, internal tools, and microservices** without the complexity of heavy frameworks.

---

## ✨ Features

✅ **Zero dependencies** – fully standalone, runs anywhere .NET runs.

✅ **Minimal & fast** – low overhead, instant startup.

✅ **JSON parsing & structured JSON/text responses** out of the box.

✅ **Lightweight DI container**:

* Singleton, Scoped, Transient lifetimes
* Auto-inject into request handlers and services

✅ **Flexible Routing**:

* Supports `GET`, `POST`, `PUT`, `DELETE`, `PATCH`, `OPTIONS`, `HEAD`
* Route parameter extraction (e.g., `/api/users/{id}`)
* Query parsing to objects (`?page=1&pageSize=10` → `QueryParams`)

✅ **Form and JSON body binding**:

* `[FromBody]`, `[FromForm]`, `[FromQuery]`, `[FromRoute]` support for handler parameters
* Automatic type binding for DTOs, primitives, and complex models

✅ **Async/Await support in handlers** for scalable IO-bound operations.

✅ **Route grouping** (`MapGroup<T>`) for modular endpoint organization.

✅ **Code-based configuration system**:

* `LiteConfiguration` with project-level initialization
* Supports `Urls`, `LaunchBrowser`, and custom `Values`
* Configurable via `builder.Configure<MyConfig>()`

✅ **Static file serving** for dashboard/admin tools.

✅ **Clear error handling** for development and production scenarios.

✅ **Predictable, clean architecture** for learning and internal tooling.

---

## 🚀 Example Usage

```csharp
using LiteAPI;

var builder = LiteWebApplication.CreateBuilder(args);
builder.Configure<MyConfiguration>();

var app = builder.Build();

app.Get("/", ctx => Response.Ok("Welcome to LiteAPI!"));

app.Get("/api/users/{id}", (HttpListenerRequest req, int id) =>
{
    var user = UserRepository.GetUser(id);
    return user != null ? Response.OkJson(user) : Response.NotFound();
});

app.Post("/api/users", (HttpListenerRequest req, [FromBody] UserDto user) =>
{
    UserRepository.AddUser(user);
    return Response.Created($"/api/users/{user.Id}", user);
});

app.Run();
```

---

## 🛠️ Advanced Features

✅ **DI usage example:**

```csharp
builder.Services.AddSingleton<IMyService, MyService>();

app.Get("/service", req =>
{
    var service = req.GetService<IMyService>();
    return Response.Ok(service.DoWork());
});
```

✅ **Query parsing:**

```csharp
app.Get("/api/items", (HttpListenerRequest req, [FromQuery] QueryParams query) =>
{
    var items = ItemRepository.GetPaged(query.Page, query.PageSize, query.Search);
    return Response.OkJson(items);
});
```

✅ **Route grouping:**

```csharp
app.MapGroup<UsersRoutes>("/api/users");
```

✅ **Static files:**

```csharp
app.MapStaticFiles(); // serves from `wwwroot/`
```

✅ **Async handlers:**

```csharp
app.Get("/api/slow", async req =>
{
    await Task.Delay(1000);
    return Response.Ok("Done!");
});
```

---

## 🛡️ Stability & Roadmap

LiteAPI is **production-friendly** for **internal tools and lightweight APIs**.

Planned features:

* Middleware pipeline (logging, auth, CORS)
* Built-in CORS support
* Graceful shutdown signals
* Optional Swagger/OpenAPI integration
* Rate limiting and caching extensions

---

## 🤝 Contributing

Pull requests are welcome! Feel free to open issues for feature requests or bugs.

---

## 🪐 License

MIT License

---

## ✉️ Contact

For collaboration, consulting, or enterprise support:

* Telegram: [@nbkabdulaxadov](https://t.me/nbkabdulaxadov)
* Email: [nbkabdulakhadov@gmail.com](mailto:nbkabdulakhadov@gmail.com)

---

**Happy building lightweight, productive APIs with LiteAPI!**
