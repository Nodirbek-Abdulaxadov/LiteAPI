# 🚀 LiteAPI

A **minimal, dependency-free C# micro web framework** for building **lightweight REST APIs, internal tools, and microservices** without the complexity of heavy frameworks.

---

## ✨ Features

✅ **Minimal & fast** – no external dependencies.

✅ **JSON parsing and responses** out of the box.  

✅ **Lightweight DI container** (Singleton, Scoped, Transient).

---

## 🗂️ Project Structure
```

LiteAPI/
│
├── Program.cs                       # Entry point
│
├── LiteAPI/
│   ├── Http/
│   │   ├── HttpListenerRequestExtensions.cs
│   │   ├── HttpMethod.cs
│   │   ├── LiteHttpContext.cs
│   │   └── Response.cs
│   │
│   ├── Routing/
│   │   ├── Router.cs
│   │   ├── RequestHandler.cs
│   │   └── LiteWebApplicationGroup.cs
│   │
│   ├── Hosting/
│   │   ├── LiteWebApplication.cs
│   │   ├── LiteWebApplicationBuilder.cs
│   │   └── LiteWebApplicationExtensions.cs
│   │
│   └── DependencyInjection/
│       ├── ServiceCollection.cs
│       ├── ServiceDescriptor.cs
│       └── ServiceLifetime.cs
│
└── LiteAPI.csproj
```

---

## 🛠️ Example Usage

```csharp
using LiteAPI;

var builder = LiteWebApplication.CreateBuilder(args);
var app = builder.Build();

app.Get("/", ctx => Response.Ok("Welcome to LiteAPI!"));

app.Run();
```

---

## 🧩 Features Roadmap

✅ Route parameter extraction

✅ Typed DI container

✅ JSON request/response handling

✅ Multi-method routing (GET, POST, PUT, DELETE)

✅ Clean error handling

### Planned:

* Middleware pipeline
* CORS support
* Route constraints and validation
* Graceful shutdown signals
* Swagger/OpenAPI integration (optional)

---

## 🤝 Contributing

Pull requests are welcome! Feel free to open issues for feature requests or bugs.

---

## 🪐 License

MIT License

---

## ✉️ Contact

For ideas or collaboration:

* Telegram: [@nbkabdulaxadov](https://t.me/nbkabdulaxadov)
* Email: [nbkabdulakhadov@gmail.com](mailto:nbkabdulakhadov@gmail.com)

---

**Happy building lightweight APIs!**

---
