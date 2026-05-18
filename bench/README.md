# LiteAPI vs Minimal API — benchmark

Three identical Todo CRUD services on the same machine, hit by the same load
generator. Compare:

| App                       | Framework                 | Port |
| ---                       | ---                       | ---  |
| `LiteApi.Managed`         | LiteAPI on `HttpListener` | 5101 |
| `LiteApi.Rust`            | LiteAPI on Rust listener  | 6080 |
| `MinimalApi`              | ASP.NET Core Minimal API  | 5103 |

All three expose the same surface and back onto a `ConcurrentDictionary`:

```
GET    /healthz
GET    /todos
GET    /todos/{id}
POST   /todos          { "title": "..." }
PUT    /todos/{id}     { "title": "...", "done": true }
DELETE /todos/{id}
POST   /echo           { "message": "..." }
```

## Run

```powershell
cd bench
./run.ps1
```

The script:
1. Builds each app in Release.
2. Starts each app, waits for `/healthz`.
3. Runs `Bench` against each: 50 000 requests, 64-way concurrency, across
   four scenarios (`healthz`, `list`, `getById`, `createPost`).
4. Prints a comparison table to stdout.

## Standalone bench

```powershell
dotnet run --project Bench/Bench.csproj -c Release -- http://127.0.0.1:5101 50000 64
```

## Notes

- Bodies are kept small (one-line JSON) on purpose: the goal is to compare
  request/dispatch overhead, not how fast each framework can serialise large
  payloads.
- `LiteApi.Rust` requires the Rust native artefact to be present at
  `LiteAPI/runtimes/win-x64/native/liteapi_rust.dll` (or platform equivalent).
  `cargo build --release` from `LiteAPI/liteapi_rust/` produces it; the
  project-reference copies it into the bench app's bin automatically.
