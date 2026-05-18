using System.Diagnostics;
using System.Net;
using System.Text;

// Usage:
//   dotnet run --project Bench -- <baseUrl> [totalRequests=50000] [concurrency=64]
//   dotnet run --project Bench -- bench-all [totalRequests] [concurrency]

var benchAll = args.Length >= 1 && args[0] == "bench-all";

if (benchAll)
{
    var total = args.Length >= 2 ? int.Parse(args[1]) : 50_000;
    var concurrency = args.Length >= 3 ? int.Parse(args[2]) : 64;

    var targets = new (string Name, string Url)[]
    {
        ("LiteAPI (managed)", "http://127.0.0.1:5101"),
        ("LiteAPI (Rust)",    "http://127.0.0.1:5102"),
        ("Minimal API",       "http://127.0.0.1:5103"),
    };

    Console.WriteLine($"Bench config: {total:N0} req × {concurrency} concurrent, per scenario, per target.\n");

    var scenarios = new (string Name, Func<HttpClient, Task<HttpResponseMessage>> Step)[]
    {
        ("GET /healthz",      http => http.GetAsync("/healthz")),
        ("GET /todos",        http => http.GetAsync("/todos")),
        ("GET /todos/1",      http => http.GetAsync("/todos/1")),
        ("POST /echo (json)", http => http.PostAsync("/echo",
            new StringContent("{\"message\":\"hi\"}", Encoding.UTF8, "application/json"))),
    };

    var results = new List<(string Scenario, string Target, BenchStats Stats)>();
    foreach (var (scName, step) in scenarios)
    {
        Console.WriteLine($"### {scName}");
        foreach (var (tgName, url) in targets)
        {
            // Fresh HttpClient per (scenario, target) so the bench process's
            // pooled state from a previous target cannot affect this run.
            // We also force a full GC between runs so allocator pressure
            // matches a cold start.
            using var http = NewClient(url);
            await SeedAsync(http);
            await WarmupAsync(http, iterations: 500);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var stats = await RunAsync(http, total, concurrency, step);
            results.Add((scName, tgName, stats));
            Console.WriteLine($"  {tgName,-22}  {stats.Rps,9:N0} rps   p50={stats.P50,6:F2}ms  p95={stats.P95,6:F2}ms  p99={stats.P99,6:F2}ms");
        }
        Console.WriteLine();
    }

    PrintFinalTable(results);
    return 0;
}

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: Bench <baseUrl> [totalRequests=50000] [concurrency=64]");
    Console.Error.WriteLine("       Bench bench-all [totalRequests] [concurrency]");
    return 1;
}

var baseUrl = args[0];
var totalSingle = args.Length >= 2 ? int.Parse(args[1]) : 50_000;
var concurrencySingle = args.Length >= 3 ? int.Parse(args[2]) : 64;

using (var http = NewClient(baseUrl))
{
    await WarmupAsync(http);
    await SeedAsync(http);
    var single = await RunAsync(http, totalSingle, concurrencySingle, h => h.GetAsync("/healthz"));
    Console.WriteLine($"GET /healthz @ {baseUrl}");
    Console.WriteLine($"  {single.Rps:N0} rps   p50={single.P50:F2}ms  p95={single.P95:F2}ms  p99={single.P99:F2}ms");
}
return 0;

// ── helpers ─────────────────────────────────────────────────────────────────

static async Task WarmupAsync(HttpClient http, int iterations = 200)
{
    for (int i = 0; i < iterations; i++)
    {
        try
        {
            using var resp = await http.GetAsync("/healthz");
            _ = await resp.Content.ReadAsByteArrayAsync();
        }
        catch { /* server may still be starting */ }
    }
}

static async Task SeedAsync(HttpClient http)
{
    for (int i = 0; i < 5; i++)
    {
        using var body = new StringContent($"{{\"title\":\"seed-{i}\"}}", Encoding.UTF8, "application/json");
        using var resp = await http.PostAsync("/todos", body);
        resp.EnsureSuccessStatusCode();
    }
}

static HttpClient NewClient(string baseUrl) => new(new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    UseCookies = false,
    AutomaticDecompression = System.Net.DecompressionMethods.None,
    // Cap the pool so the bench doesn't open thousands of connections under load
    // — represents a realistic deployment's outbound HttpClient configuration.
    MaxConnectionsPerServer = 256,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
})
{
    BaseAddress = new Uri(baseUrl),
    Timeout = TimeSpan.FromSeconds(30),
    DefaultRequestHeaders = { ConnectionClose = false },
    DefaultRequestVersion = HttpVersion.Version11,
    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
};

static async Task<BenchStats> RunAsync(HttpClient http, int total, int concurrency, Func<HttpClient, Task<HttpResponseMessage>> step)
{
    var latencies = new double[total];
    var errors = 0;
    var sem = new SemaphoreSlim(concurrency, concurrency);
    var tasks = new List<Task>(concurrency);

    var sw = Stopwatch.StartNew();
    for (int i = 0; i < total; i++)
    {
        await sem.WaitAsync();
        var slot = i;
        tasks.Add(Task.Run(async () =>
        {
            var t0 = Stopwatch.GetTimestamp();
            try
            {
                using var resp = await step(http);
                if (!resp.IsSuccessStatusCode) Interlocked.Increment(ref errors);
                _ = await resp.Content.ReadAsByteArrayAsync();
            }
            catch
            {
                Interlocked.Increment(ref errors);
            }
            finally
            {
                latencies[slot] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                sem.Release();
            }
        }));
    }
    await Task.WhenAll(tasks);
    sw.Stop();

    Array.Sort(latencies);
    var p50 = latencies[total / 2];
    var p95 = latencies[(int)(total * 0.95)];
    var p99 = latencies[(int)(total * 0.99)];
    var rps = total * 1000.0 / sw.ElapsedMilliseconds;

    return new BenchStats(total, errors, rps, p50, p95, p99);
}

static void PrintFinalTable(List<(string Scenario, string Target, BenchStats Stats)> results)
{
    Console.WriteLine();
    Console.WriteLine("┌──────────────────────┬──────────────────────┬───────────┬────────┬────────┬────────┐");
    Console.WriteLine("│ Scenario             │ Target               │       RPS │   p50  │   p95  │   p99  │");
    Console.WriteLine("├──────────────────────┼──────────────────────┼───────────┼────────┼────────┼────────┤");
    foreach (var (sc, tg, st) in results)
    {
        Console.WriteLine($"│ {sc,-20} │ {tg,-20} │ {st.Rps,9:N0} │ {st.P50,5:F2}ms │ {st.P95,5:F2}ms │ {st.P99,5:F2}ms │");
    }
    Console.WriteLine("└──────────────────────┴──────────────────────┴───────────┴────────┴────────┴────────┘");
}

record BenchStats(int Total, int Errors, double Rps, double P50, double P95, double P99);
