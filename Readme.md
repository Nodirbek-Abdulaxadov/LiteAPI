# LiteAPI

LiteAPI is a minimal, dependency-free C# micro web framework for building lightweight REST APIs, internal tools, and small services with a simple middleware pipeline, routing, and binding.

## Installation

NuGet:

```bash
dotnet add package LiteAPI.Core
```

Package Manager:

```powershell
Install-Package LiteAPI.Core
```

Requirements: **.NET 8.0** or **.NET 9.0** (the package multi-targets `net8.0;net9.0`).

## Features

- Signature-based routing with route params (including trailing wildcard `{*path}`)
- Binding: `[FromBody]`, `[FromForm]`, `[FromQuery]`, `[FromRoute]`
- Middleware pipeline (logging, CORS, auth/authz, compression, rate limiting, etc.)
- Authentication (API key / Bearer token) + Authorization (roles + policies)
- Static file serving (`app.MapStaticFiles()`)
- OpenAPI generation (`app.UseOpenApi()`)
- Production hardening defaults:
  - Concurrency limiting (`MaxConcurrentRequests`)
  - Request body size limit with **413 Payload Too Large** (`MaxRequestBodyBytes`)
  - Request body is read at most once for binding
- Observability helpers:
  - `X-Request-Id` (`app.UseRequestId()`)
  - Minimal metrics (`app.UseMetrics()`)
  - Health endpoint (`app.MapHealthz()`)

## Quick start

```csharp
using LiteAPI;
using LiteAPI.Features.Auth;

var builder = LiteWebApplication.CreateBuilder(args);

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
app.UseRequestId();
app.UseMetrics();
app.MapHealthz();

app.UseRateLimiting(maxRequests: 20, perSeconds: 10, perIp: true);
app.UseCompression(minBytes: 512);

app.UseAuthentication();
app.UseAuthorization();

app.Get("/", () => Response.Ok("Hello from LiteAPI"))
   .AllowAnonymous();

app.Get("/api/users/{id}", ([FromRoute] int id) =>
    Response.OkJson(new { id }))
   .RequireRoles("Admin");

app.Post("/echo", ([FromBody] EchoDto dto) => Response.OkJson(dto));

app.MapStaticFiles();

var options = new LiteServerOptions
{
    MaxConcurrentRequests = 128,
    MaxRequestBodyBytes = 64 * 1024
};

// Managed hosting (HttpListener)
// app.Run(options);

// Rust TCP listener hosting (same middleware/router pipeline)
app.RunWithRust(options);

public record EchoDto(string Message);
```

## Hosting modes

LiteAPI can run in two modes:

- **Managed (default):** `app.Run(...)` uses `HttpListener`.
- **Rust listener:** `app.RunWithRust(...)` uses an embedded Rust TCP listener that parses HTTP, calls into the same managed middleware/router pipeline, then returns a full HTTP response.

### Cross-platform native packaging

The NuGet package can ship the Rust native library under `runtimes/<rid>/native/` (CI/release builds).
Currently supported RIDs:

- `win-x64`, `win-arm64`
- `linux-x64`, `linux-arm64`
- `osx-x64`, `osx-arm64`

## Handler parameter guidelines (important)

- Prefer **host-independent** handler signatures (works for both managed and Rust modes):
  - primitives + `[FromRoute]` / `[FromQuery]`
  - DTOs via `[FromBody]` / `[FromForm]`
  - `LiteAPI.Http.LiteRequest` if you need headers/query/body stream
- `HttpListenerRequest` is **only available in managed mode**; in Rust mode it is not present.

## Contributing / building native locally

To build and copy the Rust native library into the correct `runtimes/<rid>/native/` folder:

- Windows: `powershell -File scripts/build-rust-native.ps1 -Rid win-x64`
- Linux/macOS: `bash scripts/build-rust-native.sh linux-x64` (or `osx-arm64`, etc.)

## Graceful shutdown

`LiteWebApplication.RunAsync(options, cancellationToken)` accepts a token and also installs a `Ctrl+C` handler. It stops accepting new requests, drains in-flight requests for up to 10 seconds, then closes the listener. Call `StopAsync()` from anywhere (e.g. an admin endpoint) to trigger the same shutdown path.

```csharp
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await app.RunAsync(new LiteServerOptions
{
    MaxConcurrentRequests = 128,
    MaxRequestBodyBytes   = 64 * 1024
}, cts.Token);
```

## Streaming responses

```csharp
// Server-Sent Events
app.Get("/events", () => Response.Sse(EventStream()));

// File streaming (no full-buffer allocation)
app.Get("/files/{name}", (string name) => Response.File($"./uploads/{name}"));

// Custom writer
app.Get("/big", () => Response.Stream(async (stream, ct) =>
{
    for (int i = 0; i < 1000; i++)
        await stream.WriteAsync($"chunk {i}\n"u8.ToArray(), ct);
}, "text/plain"));
```

## Response caching

```csharp
app.UseResponseCaching(o =>
{
    o.TtlSeconds = 30;
    o.VaryByHeaders = new[] { "Accept-Language" };
});

app.Get("/expensive", () => Response.OkJson(ExpensiveLookup()));  // cached for 30s
```

Caches `GET`/`HEAD` 200s by `method | path | query | vary-by`. Adds `X-Cache: HIT/MISS`. Honours `Cache-Control: no-store`.

## Validation

```csharp
builder.AddValidation();   // opt-in DataAnnotations

public record Dto([Required] string Name, [Range(1,120)] int Age);

app.Post("/users", ([FromBody] Dto dto) => Response.OkJson(dto));
// invalid body → 400 with RFC 7807 problem+json
```

`[SkipValidation]` on a handler opts out; the response shape can be switched to a compact `{errors:[...]}` form or fully overridden via `ValidationOptions.CustomResponse`.

## File uploads

```csharp
public class UploadDto
{
    public string? Description { get; set; }
    public MultipartPart? Avatar { get; set; }   // single file
    public List<MultipartPart>? Photos { get; set; }  // multiple files under same name
}

app.Post("/upload", ([FromForm] UploadDto dto) => Response.OkJson(new {
    desc = dto.Description,
    avatar = dto.Avatar?.FileName,
    photos = dto.Photos?.Count
}));
```

Properties typed as `MultipartPart`, `byte[]`, `List<MultipartPart>` or `IReadOnlyList<MultipartPart>` receive uploaded files; everything else still binds from text fields.

## Roadmap

Next planned (high-level):

- CLI scaffolding (`dotnet new lite-api`)
- True chunked transfer over the Rust listener
- HTTPS in managed mode

## License

MIT License.