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
    
    public static LiteHttpContext GetContext(this HttpListenerContext context, Dictionary<string, string>? routeParams = null)
    {
        return new LiteHttpContext(context, routeParams);
    }
}