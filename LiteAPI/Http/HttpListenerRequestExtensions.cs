using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace LiteAPI;

public static class HttpListenerRequestExtensions
{
    private class RequestScope(ServiceCollection services)
    {
        public ServiceCollection Services { get; } = services;
        public Dictionary<Type, object> ScopedInstances { get; } = [];

        public TService GetService<TService>() where TService : class
        {
            return (TService)Services.Resolve(typeof(TService), ScopedInstances);
        }
    }

    private static readonly ConditionalWeakTable<HttpListenerRequest, RequestScope> _scopes = new();

    internal static void SetServices(this HttpListenerRequest request, ServiceCollection services)
    {
        _scopes.Add(request, new RequestScope(services));
    }

    public static TService GetService<TService>(this HttpListenerRequest request) where TService : class
    {
        if (_scopes.TryGetValue(request, out var scope))
        {
            return scope.GetService<TService>();
        }
        throw new InvalidOperationException("ServiceCollection not found for this request.");
    }

    public static T? ReadJsonBody<T>(this HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream);
        var body = reader.ReadToEnd();
        return JsonSerializer.Deserialize<T>(body);
    }

    public static LiteHttpContext GetContext(this HttpListenerRequest request, Dictionary<string, string>? routeParams = null)
    {
        return new LiteHttpContext(request, routeParams ?? []);
    }

    public static T GetFromQuery<T>(this HttpListenerRequest request)
    {
        var obj = Activator.CreateInstance<T>()!;
        var props = typeof(T).GetProperties();

        var query = System.Web.HttpUtility.ParseQueryString(request.Url!.Query);

        foreach (var prop in props)
        {
            var value = query.Get(prop.Name);
            if (value != null)
            {
                var converted = Convert.ChangeType(value, prop.PropertyType, CultureInfo.InvariantCulture);
                prop.SetValue(obj, converted);
            }
        }

        return obj;
    }

    public static T GetFromRoute<T>(this LiteHttpContext ctx)
    {
        var obj = Activator.CreateInstance<T>()!;
        var props = typeof(T).GetProperties();

        foreach (var prop in props)
        {
            if (ctx.Params.TryGetValue(prop.Name, out var value))
            {
                var converted = Convert.ChangeType(value, prop.PropertyType, CultureInfo.InvariantCulture);
                prop.SetValue(obj, converted);
            }
        }

        return obj;
    }

    public static T GetFromBody<T>(this HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream);
        var body = reader.ReadToEnd();
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        return JsonSerializer.Deserialize<T>(body, options)!;
    }

    public static T GetFromForm<T>(this HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream);
        var body = reader.ReadToEnd();
        var parsed = System.Web.HttpUtility.ParseQueryString(body);

        var obj = Activator.CreateInstance<T>()!;
        var props = typeof(T).GetProperties();

        foreach (var prop in props)
        {
            var value = parsed.Get(prop.Name);
            if (value != null)
            {
                var converted = Convert.ChangeType(value, prop.PropertyType, CultureInfo.InvariantCulture);
                prop.SetValue(obj, converted);
            }
        }

        return obj;
    }


}