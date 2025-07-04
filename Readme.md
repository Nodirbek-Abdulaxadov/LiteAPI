# 🚀 LiteAPI

A **minimal, dependency-free C# micro web framework** for building **lightweight REST APIs, dashboards, internal tools, and microservices** without the complexity of heavy frameworks.

---

## 📦 Installation

Install via **NuGet**:

```bash
dotnet add package LiteAPI.Core --version 1.1.0
```

Or via **Package Manager**:

```powershell
Install-Package LiteAPI.Core -Version 1.1.0
```

Ready for **.NET 6, 7, 8 LTS**.

---

## ✨ Features

✅ **Zero dependencies** – fully standalone, tiny, fast.

✅ **JSON + text responses out of the box**.

✅ **Lightweight DI container** (Singleton, Scoped, Transient).

✅ **Signature-based routing with parameter extraction**.

✅ **Automatic model binding:**

* `[FromBody]`, `[FromForm]`, `[FromQuery]`, `[FromRoute]`.


✅ **Async/await handler support**.

✅ **Middleware pipeline** (`app.UseLogging()`, `app.UseCors()`, etc).

✅ **Authentication & Authorization**:

* API Key, Bearer Token auth.
* Policy and role-based route protection.

✅ **Route grouping for modular structure**.

✅ **Static file serving** (`app.MapStaticFiles()`).

✅ **Optional OpenAPI (Swagger) generation** for testing endpoints.

✅ **Launch browser on startup** for local dashboards.

✅ **Clean, readable structure with intuitive extension methods**.

✅ **No black-box magic, easy to learn and extend**.

---

## 🚀 Quick Example

```csharp
using LiteAPI;

var builder = LiteWebApplication.CreateBuilder(args);
builder.Configure<MyConfiguration>();

builder.AddAuthentication(auth =>
{
    auth.DefaultScheme = AuthScheme.Bearer;
    auth.ValidateBearerToken = token => token == "secret-token";
});

builder.AddAuthorization(authz =>
{
    authz.AddPolicy("AdminOnly", ctx =>
        ctx.Headers.TryGetValue("X-Role", out var role) && role == "Admin");
});

var app = builder.Build();
app.UseLogging();
app.UseAuthentication();
app.UseAuthorization();

app.Get("/", ctx => Response.Ok("Welcome to LiteAPI 🚀"));

app.Get("/api/users/{id}", (HttpListenerRequest req, [FromRoute] int id) =>
{
    var userService = req.GetService<UserService>();
    var user = userService.GetById(id);
    return user != null ? Response.OkJson(user) : Response.NotFound();
}).RequireRoles("Admin");

app.Post("/api/users", (HttpListenerRequest req, [FromBody] UserDto user) =>
{
    var userService = req.GetService<UserService>();
    var created = userService.Add(user);
    return Response.Created($"/api/users/{created.Id}", created);
});

app.Run();
```

---

## 🛠️ Advanced Usage

✅ **Dependency Injection:**

```csharp
builder.Services.AddSingleton<UserService>();
```

✅ **Query parsing:**

```csharp
app.Get("/api/items", (HttpListenerRequest req, [FromQuery] QueryParams query) =>
{
    var items = ItemService.GetPaged(query.Page, query.PageSize, query.Search);
    return Response.OkJson(items);
});
```

✅ **Route grouping:**

```csharp
app.MapGroup<UsersRoutes>("/api/users");
```

✅ **Middleware pipeline:**

```csharp
app.UseLogging();
app.UseCors();
app.UseExceptionHandling();
```

✅ **Async handlers:**

```csharp
app.Get("/delay", async ctx =>
{
    await Task.Delay(1000);
    return Response.Ok("Done!");
});
```

✅ **Static files:**

```csharp
app.MapStaticFiles();
```

✅ **OpenAPI (Swagger):**

```csharp
app.UseOpenApi();
```

---

## 🪐 Roadmap

✅ Middleware pipeline

✅ Auth & policy-based authorization

✅ OpenAPI (Swagger) support

✅ Route grouping & parameter binding

✅ Static file serving

✅ Async pipeline

✅ Lightweight DI

✅ Rate limiting middleware

**Next planned:**

* Caching middleware.
* Request validation extensions.
* CLI scaffolding for generating LiteAPI projects quickly.

---

## 🤝 Contributing

Pull requests and discussions are welcome!

✅ Add examples

✅ Improve documentation

✅ Suggest advanced DI features

---

## 🪐 License

MIT License.

---

## ✉️ Contact

**Author:** [@nbkabdulaxadov](https://t.me/nbkabdulaxadov) on Telegram

**Email:** [nbkabdulakhadov@gmail.com](mailto:nbkabdulakhadov@gmail.com)

---

**Start building clean, fast, lightweight APIs with LiteAPI today 🚀!**

---