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

    public static T GetValue<T>(this LiteHttpContext ctx, string paramName)
    {
        if (!ctx.Params.TryGetValue(paramName, out var value))
            throw new InvalidOperationException($"Route parameter '{paramName}' not found.");

        try
        {
            if (typeof(T) == typeof(string))
                return (T)(object)value;

            if (typeof(T) == typeof(int) && int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var intValue))
                return (T)(object)intValue;

            if (typeof(T) == typeof(long) && long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var longValue))
                return (T)(object)longValue;

            if (typeof(T) == typeof(Guid) && Guid.TryParse(value, out var guidValue))
                return (T)(object)guidValue;

            if (typeof(T) == typeof(bool) && bool.TryParse(value, out var boolValue))
                return (T)(object)boolValue;

            if (typeof(T) == typeof(double) && double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue))
                return (T)(object)doubleValue;

            // Extend for other primitives as needed

            throw new InvalidCastException($"Cannot convert '{value}' to type {typeof(T).Name}.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error parsing parameter '{paramName}': {ex.Message}");
        }
    }
}