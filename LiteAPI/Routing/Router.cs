using System.Net;
using System.Text;

namespace LiteAPI;

/// <summary>
/// Minimal router for LiteAPI, mapping method + path to handlers.
/// </summary>
public class Router
{
    private readonly Dictionary<(string method, string path), RequestHandler> routes = [];

    private void Handle(string method, string path, RequestHandler handler) =>
        routes[(method.ToUpperInvariant(), path)] = handler;

    public void Get(string path, RequestHandler handler) => Handle("GET", path, handler);
    public void Post(string path, RequestHandler handler) => Handle("POST", path, handler);
    public void Put(string path, RequestHandler handler) => Handle("PUT", path, handler);
    public void Delete(string path, RequestHandler handler) => Handle("DELETE", path, handler);
    public void Patch(string path, RequestHandler handler) => Handle("PATCH", path, handler);
    public void Options(string path, RequestHandler handler) => Handle("OPTIONS", path, handler);
    public void Head(string path, RequestHandler handler) => Handle("HEAD", path, handler);

    public Response Route(HttpListenerRequest request)
    {
        string method = request.HttpMethod.ToUpperInvariant();
        string path = request.Url!.AbsolutePath;

        if (routes.TryGetValue((method, path), out var handler))
        {
            try
            {
                return handler(request);
            }
            catch (Exception ex)
            {
                return Response.BadRequest(ex.Message);
            }
        }

        return new Response
        {
            StatusCode = 404,
            ContentType = "text/plain",
            Body = Encoding.UTF8.GetBytes("Not Found")
        };
    }
}