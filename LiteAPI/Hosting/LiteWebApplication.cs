/// <summary>
/// LiteWebApplication: signature-based routing with delegate support and auth.
/// </summary>
public class LiteWebApplication(
    Router router,
    ServiceCollection services,
    string[] urls,
    AuthenticationOptions authOptions,
    AuthorizationOptions authorizationOptions)
{
    private readonly HttpListener _listener = new();
    private readonly List<LiteMiddleware> _middlewares = [];
    private readonly CancellationTokenSource _shutdownCts = new();

    public Router Router => router;
    public ServiceCollection Services => services;
    public AuthenticationOptions AuthOptions => authOptions;
    public AuthorizationOptions AuthorizationOptions => authorizationOptions;

    public static LiteWebApplicationBuilder CreateBuilder(string[] args) => new();

    #region Handlers
    public RouteDefinition Get(string path, Delegate handler) => router.Get(path, handler);
    public RouteDefinition Post(string path, Delegate handler) => router.Post(path, handler);
    public RouteDefinition Put(string path, Delegate handler) => router.Put(path, handler);
    public RouteDefinition Delete(string path, Delegate handler) => router.Delete(path, handler);
    public RouteDefinition Patch(string path, Delegate handler) => router.Patch(path, handler);
    public RouteDefinition Options(string path, Delegate handler) => router.Options(path, handler);
    public RouteDefinition Head(string path, Delegate handler) => router.Head(path, handler);
    public RouteDefinition Get(string path, RequestHandler handler) => router.Get(path, handler);
    public RouteDefinition Post(string path, RequestHandler handler) => router.Post(path, handler);
    public RouteDefinition Put(string path, RequestHandler handler) => router.Put(path, handler);
    public RouteDefinition Delete(string path, RequestHandler handler) => router.Delete(path, handler);
    public RouteDefinition Patch(string path, RequestHandler handler) => router.Patch(path, handler);
    public RouteDefinition Options(string path, RequestHandler handler) => router.Options(path, handler);
    public RouteDefinition Head(string path, RequestHandler handler) => router.Head(path, handler);
    #endregion

    public void Use(LiteMiddleware middleware) => _middlewares.Add(middleware);

    /// <summary>Use a middleware class implementing ILiteMiddleware.</summary>
    public void Use<T>() where T : ILiteMiddleware, new()
    {
        var instance = new T();
        _middlewares.Add(instance.InvokeAsync);
    }

    /// <summary>
    /// Signals every <c>Run*</c> loop to stop accepting new requests and let
    /// in-flight requests finish. Safe to call from a signal handler.
    /// </summary>
    public void StopAsync()
    {
        try { _shutdownCts.Cancel(); } catch { }
    }

    public void Run() => Run(new LiteServerOptions());

    internal async Task<Response> ProcessRequestAsync(
        LiteHttpContext liteContext,
        RouteDefinition? matchedRoute,
        Dictionary<string, string> routeParams,
        LiteServerOptions options)
    {
        async Task ExecuteMiddleware(int index)
        {
            if (index < _middlewares.Count)
                await _middlewares[index](liteContext, () => ExecuteMiddleware(index + 1));
            else
                liteContext.Response = matchedRoute is null
                    ? Response.NotFound()
                    : await router.InvokeAsync(matchedRoute, liteContext.Request, routeParams, options.MaxRequestBodyBytes);
        }

        await ExecuteMiddleware(0);
        return liteContext.Response ?? Response.NotFound();
    }

    public void Run(LiteServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxConcurrentRequests <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxConcurrentRequests), "Must be > 0.");

        RunAsync(options).GetAwaiter().GetResult();
    }

    public async Task RunAsync(LiteServerOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new LiteServerOptions();
        using var concurrency = new SemaphoreSlim(options.MaxConcurrentRequests, options.MaxConcurrentRequests);

        foreach (var url in urls)
            _listener.Prefixes.Add(url.EndsWith('/') ? url : url + "/");

        _listener.Start();
        Console.WriteLine($"LiteAPI listening on: {string.Join(", ", urls)}");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            try { _shutdownCts.Cancel(); } catch { }
        };

        var inFlight = new List<Task>();

        try
        {
            while (!linkedCts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (HttpListenerException) { break; }

                context.Request.SetServices(services);

                try
                {
                    await concurrency.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    SafeClose(context);
                    break;
                }

                var task = Task.Run(async () =>
                {
                    try
                    {
                        await HandleContextAsync(context, options);
                    }
                    finally
                    {
                        concurrency.Release();
                    }
                });

                lock (inFlight) inFlight.Add(task);
                _ = task.ContinueWith(t => { lock (inFlight) inFlight.Remove(t); }, TaskScheduler.Default);
            }
        }
        finally
        {
            Task[] pending;
            lock (inFlight) pending = [.. inFlight];

            try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10)); }
            catch { /* drain timeout — proceed with close */ }

            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, LiteServerOptions options)
    {
        try
        {
            var request = context.Request;

            if (options.MaxRequestBodyBytes is long maxBytes
                && request.ContentLength64 > 0
                && request.ContentLength64 > maxBytes)
            {
                var tooLarge = Response.PayloadTooLarge($"Request body exceeds limit ({maxBytes} bytes).");
                await WriteResponseAsync(context.Response, tooLarge);
                return;
            }

            var method = request.HttpMethod.ToUpperInvariant();
            var path = request.Url!.AbsolutePath;

            router.TryResolve(method, path, out var matchedRoute, out var routeParams);

            var liteContext = context.GetContext(routeParams);
            liteContext.RouteMetadata = matchedRoute?.Metadata ?? new RouteMetadata { AllowAnonymous = true };

            var response = await ProcessRequestAsync(liteContext, matchedRoute, routeParams, options);

            context.Response.StatusCode = response.StatusCode;
            context.Response.ContentType = response.ContentType;

            foreach (var header in liteContext.ResponseHeaders)
            {
                // ContentType/Length are properties on HttpListenerResponse;
                // setting via Headers collection is rejected at runtime.
                if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                context.Response.Headers[header.Key] = header.Value;
            }

            if (response.IsStreaming)
            {
                // Streaming: no Content-Length; flush incremental writes.
                context.Response.SendChunked = true;
                await response.StreamWriter!(context.Response.OutputStream, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                context.Response.ContentLength64 = response.Body.Length;
                await context.Response.OutputStream.WriteAsync(response.Body).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            try { context.Response.StatusCode = 500; } catch { }
        }
        finally
        {
            SafeClose(context);
        }
    }

    private static async Task WriteResponseAsync(HttpListenerResponse rawResponse, Response response)
    {
        rawResponse.StatusCode = response.StatusCode;
        rawResponse.ContentType = response.ContentType;
        rawResponse.ContentLength64 = response.Body.Length;
        if (response.Body.Length > 0)
            await rawResponse.OutputStream.WriteAsync(response.Body).ConfigureAwait(false);
    }

    private static void SafeClose(HttpListenerContext context)
    {
        try { context.Response.OutputStream.Close(); } catch { }
    }

    public void RunWithRust() => RunWithRust(new LiteServerOptions());

    public void RunWithRust(LiteServerOptions options)
    {
        Console.WriteLine("Running LiteAPI using embedded Rust TCP listener...");
        RustBridge.StartRustListener(this, options);
    }
}
