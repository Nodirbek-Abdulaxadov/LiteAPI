using LiteAPI.Interop;

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

    /// <summary>
    /// Use a middleware class implementing ILiteMiddleware.
    /// </summary>
    public void Use<T>() where T : ILiteMiddleware, new()
    {
        var instance = new T();
        _middlewares.Add(instance.InvokeAsync);
    }

    public void Run()
    {
        foreach (var url in urls)
            _listener.Prefixes.Add(url.EndsWith('/') ? url : url + "/");

        _listener.Start();
        Console.WriteLine($"LiteAPI running on: {string.Join(", ", urls)}");

        while (true)
        {
            var context = _listener.GetContext();
            context.Request.SetServices(services);

            _ = Task.Run(async () =>
            {
                try
                {
                    var liteContext = context.GetContext();

                    async Task ExecuteMiddleware(int index)
                    {
                        if (index < _middlewares.Count)
                            await _middlewares[index](liteContext, () => ExecuteMiddleware(index + 1));
                        else
                            liteContext.Response = await router.RouteAsync(context.Request);
                    }

                    await ExecuteMiddleware(0);

                    var response = liteContext.Response ?? Response.NotFound();
                    context.Response.StatusCode = response.StatusCode;
                    context.Response.ContentType = response.ContentType;
                    context.Response.ContentLength64 = response.Body.Length;
                    context.Response.OutputStream.Write(response.Body, 0, response.Body.Length);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex}");
                    context.Response.StatusCode = 500;
                }
                finally
                {
                    context.Response.OutputStream.Close();
                }
            });
        }
    }

    public void RunWithRust()
    {
        Console.WriteLine("🦀 Running LiteAPI using embedded Rust TCP listener...");
        RustBridge.StartRustListener(router); // marshrutlaydi: method + path + body
    }
}
