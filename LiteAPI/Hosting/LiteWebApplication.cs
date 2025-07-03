using LiteAPI.Middlewares;
using System.Net;

namespace LiteAPI;

/// <summary>
/// LiteWebApplication: signature-based routing with delegate support.
/// </summary>
public class LiteWebApplication(Router router, ServiceCollection services, string[] urls)
{
    private readonly HttpListener _listener = new();
    private readonly List<LiteMiddleware> _middlewares = [];

    public static LiteWebApplicationBuilder CreateBuilder(string[] args) => new();

    public Router Router => router;
    public ServiceCollection Services => services;

    #region Handlers
    public void Get(string path, Delegate handler) => router.Get(path, handler);
    public void Post(string path, Delegate handler) => router.Post(path, handler);
    public void Put(string path, Delegate handler) => router.Put(path, handler);
    public void Delete(string path, Delegate handler) => router.Delete(path, handler);
    public void Patch(string path, Delegate handler) => router.Patch(path, handler);
    public void Options(string path, Delegate handler) => router.Options(path, handler);
    public void Head(string path, Delegate handler) => router.Head(path, handler);
    public void Get(string path, RequestHandler handler) => router.Get(path, handler);
    public void Post(string path, RequestHandler handler) => router.Post(path, handler);
    public void Put(string path, RequestHandler handler) => router.Put(path, handler);
    public void Delete(string path, RequestHandler handler) => router.Delete(path, handler);
    public void Patch(string path, RequestHandler handler) => router.Patch(path, handler);
    public void Options(string path, RequestHandler handler) => router.Options(path, handler);
    public void Head(string path, RequestHandler handler) => router.Head(path, handler); 
    #endregion

    public void Run()
    {
        foreach (var url in urls)
        {
            var fixedUrl = url.EndsWith('/') ? url : url + "/";
            _listener.Prefixes.Add(fixedUrl);
        }

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
                        {
                            await _middlewares[index](liteContext, () => ExecuteMiddleware(index + 1));
                        }
                        else
                        {
                            var response = await router.RouteAsync(context.Request);
                            liteContext.Response = response;
                        }
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

    public void Use(LiteMiddleware middleware)
    {
        _middlewares.Add(middleware);
    }
}