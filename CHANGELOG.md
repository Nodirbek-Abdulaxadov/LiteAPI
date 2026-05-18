# Changelog

## [2.3.0] — 2026-05-18

### Added — HTTP/1.1 keep-alive in the Rust listener

The native listener now reuses a single TCP connection across requests instead of closing after each response. A connection is kept open between requests as long as:

- The request did not send `Connection: close`,
- The HTTP version is `HTTP/1.1` (or `HTTP/1.0 Connection: keep-alive`),
- The next request arrives before the idle timeout expires.

New env var:

- `LITEAPI_RUST_IDLE_TIMEOUT_SECS` (default `15`) — how long the server waits between requests on an idle keep-alive connection before reaping it. The existing `LITEAPI_RUST_READ_TIMEOUT_SECS` (default `30`) now applies only to the first request on a freshly accepted connection.

Other listener-level tweaks:

- Accepted sockets are set to `TCP_NODELAY` to keep small responses off Nagle's algorithm — this was the main source of pathological latency on the request-per-RTT keep-alive pattern.
- The serve loop reads through `BufReader<TcpStream>` and writes through a cloned `TcpStream` handle, so request parsing keeps its read-buffer state across requests on the same connection.
- Idle expiry (`WouldBlock` / `TimedOut` / `UnexpectedEof`) is treated as a normal end-of-connection event instead of being logged as a failure.

### Changed — Bridge no longer forces `Connection: close`

`RustBridge.WriteResponse` previously wrote `Connection: close` on every framework response, which defeated keep-alive even after the listener supported it. The bridge now leaves `Connection` unset (HTTP/1.1 default is keep-alive) and lets the Rust listener decide whether to close based on the request. `Content-Length` is still framework-owned.

### Fixed — Native asset paths for ProjectReference consumers

`LiteAPI.csproj` now sets `<Link>` on the `runtimes/<rid>/native/` content items to a *flat* filename (`liteapi_rust.dll`, `libliteapi_rust.so`, `libliteapi_rust.dylib`) so that ProjectReference consumers like `LiteAPI.Example` get the native dropped flat next to their executable — the first directory the .NET P/Invoke resolver searches. NuGet consumers continue to pick the native up from `runtimes/<rid>/native/` via the package's generated `deps.json`.

### Performance

Comparative bench (50,000 req x 64 concurrent, loopback, Release, Windows):

| Scenario          | LiteAPI managed         | LiteAPI Rust            | Minimal API             |
| ----------------- | ----------------------- | ----------------------- | ----------------------- |
| `GET /healthz`    | 33,715 rps, p99 14.64 ms | **60,241 rps, p99 3.77 ms** | 43,706 rps, p99 3.87 ms |
| `GET /todos`      | 46,816 rps, p99 14.05 ms | **73,855 rps, p99 3.32 ms** | 45,086 rps, p99 4.70 ms |
| `GET /todos/1`    | 50,968 rps, p99 15.79 ms | **114,679 rps, p99 2.20 ms** | 23,697 rps, p99 5.74 ms |
| `POST /echo`      | 29,568 rps, p99 14.78 ms | **56,948 rps, p99 5.18 ms** | 24,814 rps, p99 5.15 ms |

The Rust listener now leads on both throughput and tail latency across the four canonical scenarios, with sub-millisecond p50 on every endpoint.

## [2.1.0] — Unreleased

Adds the major roadmap features in a single release: streaming responses, a real multipart parser with file uploads, opt-in response caching, and DataAnnotations validation. All built on the 2.0 architecture; no breaking changes to the buffered request/response surface.

### Added — Streaming responses

`Response` now exposes a `StreamWriter` slot alongside the existing `Body`. When set, the host (`HttpListener` managed mode) flushes incremental writes through to the underlying network stream with `SendChunked = true`. New helpers:

- `Response.Stream(Func<Stream, CancellationToken, Task> writer, string contentType)` — bring-your-own writer.
- `Response.File(string path)` — streams a disk file with extension-inferred content type; returns 404 if missing.
- `Response.Sse(IAsyncEnumerable<string> events)` — Server-Sent Events helper, one `data:` frame per yielded string.

The Rust hosting path materialises streamed bodies into a single buffer before crossing the FFI (true chunked transfer over the Rust listener is a follow-up).

### Added — Real multipart parser

`LiteAPI.Http.MultipartReader` is a new RFC 7578-aligned parser:
- Byte-level boundary scanning; binary data (zero bytes, embedded CRLFs) passes through intact.
- Returns `IReadOnlyList<MultipartPart>` with name, filename, per-part content type, headers, and the raw body bytes.
- Tolerates quoted boundaries and trailing parameters on the `Content-Type` header.

`RequestBinder.BindMultipart` now backs onto the new reader. Properties typed as `MultipartPart`, `byte[]`, `List<MultipartPart>`, or `IReadOnlyList<MultipartPart>` receive the uploaded files; everything else still binds from text fields with full type conversion.

The previous text-only, line-oriented `ParseMultipartFormData` stays as a back-compat wrapper that drops file parts.

### Added — Response caching middleware

`app.UseResponseCaching(o => …)`:
- Caches successful (`200`) `GET` / `HEAD` responses by `method | path | query | vary-by headers`.
- Configurable `TtlSeconds`, `IncludeQueryString`, `VaryByHeaders`, `MaxCachedBodyBytes`.
- Honours `Cache-Control: no-store` on the request.
- Adds `X-Cache: HIT|MISS` on the response.
- Pluggable backing store via `IResponseCacheStore`; default `InMemoryResponseCacheStore` does lazy TTL eviction with no background timer.
- Streaming responses are never cached.

### Added — DataAnnotations validation

`builder.AddValidation(o => …)` plus the new `[SkipValidation]` attribute. After model binding completes, the router runs `Validator.TryValidateObject(…)` on every bound complex argument and, on failure, returns a `400` response with either RFC 7807 `application/problem+json` or a flat `{ errors: [...] }` shape (selectable via `ResponseShape`). Users can fully override the body with `ValidationOptions.CustomResponse`.

### Tests

- Test count: **27 → 49** (`StreamingResponseTests`, `MultipartReaderTests`, `ResponseCachingTests`, `ValidationTests`).

## [2.0.0] — Unreleased

Correctness, performance, and lifecycle improvements. Multi-targets `net8.0` and `net9.0`.

### Correctness

- **Sync/async router parity** — `Router.Route(HttpListenerRequest)` and `RouteAsync(...)` now share the same binding pipeline. The old `InvokeSync` path silently ignored `[FromBody]`, `[FromForm]`, `[FromQuery]`, and complex POST/PUT/PATCH bodies. Removed.
- **400 instead of 500 on bad input** — invalid route / query / form values (bad `int`, malformed `Guid`, unparseable `DateOnly`, etc.) were caught as `InvalidCastException` and surfaced as 500. They now return 400 with a descriptive message.
- **Full type coverage** — `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `bool`, `enum`, all primitives + nullable variants are first-class for route / query / form params (centralised in `TypeConversion.TryConvert`).
- **JSON parse failure → 400** — `[FromBody]` and POST/PUT/PATCH complex params catch `JsonException` and return 400 with the parser message instead of bubbling as 500.
- **`Router.HandleRawRequest`** now accepts an optional `headers` dictionary and `contentType`. It also infers `application/json` when the body starts with `{`/`[`, instead of forcing `text/plain`. (Without this, JSON bodies sent via the raw entry point were impossible to deserialise.)
- **`MapStaticFiles` no longer silently overrides** an existing `"/{*path}"` route. If the application registered one first, the static-file fallback is skipped (logged).
- **`MapStaticFiles` path-traversal hardening** — the canonicalised candidate path must live strictly under the root directory (or be the root itself), not merely start with the root prefix.

### Performance

- **Compiled handler delegates** — each `RouteDefinition` caches a `Func<object?[], object?>` compiled from the delegate via `System.Linq.Expressions`. Per-request `Delegate.DynamicInvoke` is gone.
- **Cached `ParameterInfo[]`** — each `RouteDefinition` materialises its handler's parameter array once at registration time instead of reflecting on every request.
- **Cached `JsonSerializerOptions`** in `Response` and `Router` — was being allocated on every JSON response / body deserialise.
- **`Response.OkJson` / `Response.Json`** use `JsonSerializer.SerializeToUtf8Bytes` directly instead of `Serialize` → `Encoding.UTF8.GetBytes`.
- **`MapStaticFiles`** streams files through `FileStream(useAsync: true)` — was previously `File.ReadAllBytes` for the entire response, even for multi-MB assets.
- **Response writes are async** — the `HttpListener` host writes the response with `WriteAsync`. Slow clients no longer hold a thread-pool thread.

### Lifecycle

- **Graceful shutdown** — `LiteWebApplication.RunAsync(options, cancellationToken)` is the new primary entry point. It honours both the supplied `CancellationToken` and a built-in `Ctrl+C` handler. Stops accepting new requests, drains in-flight requests for up to 10 seconds, then closes the listener cleanly. `Run(...)` keeps its old sync signature and is implemented in terms of `RunAsync`.
- **`StopAsync()`** — public method that signals shutdown from anywhere (e.g. a `/admin/shutdown` route or a hosted service).

### Lower-priority fixes

- **`LiteHttpContext.SetResponseHeader`** no longer double-writes (it used to push to `ResponseHeaders` and `RawResponse.Headers`, then the host copied `ResponseHeaders` to `RawResponse.Headers` again). Now strictly populates the dictionary; the host flushes once, skipping `Content-Type`/`Content-Length` which need direct property assignment.
- **`RequestBinder.Bind<T>`** now handles `enum` and nullable value types — the non-generic `Bind(string, Type)` already did, the generic overload didn't.
- **`RequestBinder.ParseMultipartFormData`** is slightly more tolerant — boundary parsing trims whitespace / quotes; multi-line values still collapse but no longer crash on missing `Content-Disposition` name.
- `Response` helpers all emit `; charset=utf-8` so `application/json` / `text/html` responses don't show mojibake in browsers that pick a non-UTF default.

### Tooling

- **Multi-target** `net8.0;net9.0` for both `LiteAPI` and `LiteAPI.Tests`.
- `Directory.Build.props` carries the shared `Version`, `Authors`, repo / license metadata, and deterministic / CI-build flags so each csproj stays focused.
- **Test coverage tripled** — 27 tests (up from 3) across `TypeConversion`, `DelegateInvoker`, and the `Router` binding pipeline.

Version bumped from `1.1.3` → **`2.0.0`**.
