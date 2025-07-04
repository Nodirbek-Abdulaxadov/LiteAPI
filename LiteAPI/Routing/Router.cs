/// <summary>
/// Minimal router for LiteAPI with signature-based routing.
/// </summary>
public class Router
{
    private readonly Dictionary<(string method, string path), RouteDefinition> routes = [];
    private readonly Dictionary<(string method, string path), RouteMetadata> _routeMetadata = [];
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public RouteDefinition Get(string path, Delegate handler) => Handle("GET", path, handler);
    public RouteDefinition Post(string path, Delegate handler) => Handle("POST", path, handler);
    public RouteDefinition Put(string path, Delegate handler) => Handle("PUT", path, handler);
    public RouteDefinition Delete(string path, Delegate handler) => Handle("DELETE", path, handler);
    public RouteDefinition Patch(string path, Delegate handler) => Handle("PATCH", path, handler);
    public RouteDefinition Options(string path, Delegate handler) => Handle("OPTIONS", path, handler);
    public RouteDefinition Head(string path, Delegate handler) => Handle("HEAD", path, handler);
    public Dictionary<(string method, string path), RouteDefinition> GetRoutes() => routes;

    private RouteDefinition Handle(string method, string path, Delegate handler)
    {
        var def = new RouteDefinition(method.ToUpperInvariant(), path, handler);
        routes[(def.Method, def.Path)] = def;
        return def;
    }
    public Response Route(HttpListenerRequest request)
    {
        string method = request.HttpMethod.ToUpperInvariant();
        string path = request.Url!.AbsolutePath;

        foreach (var route in routes)
        {
            var (routeMethod, routePath) = route.Key;
            if (routeMethod != method) continue;

            if (TryMatchRoute(path, routePath, out var routeParams))
            {
                var routeDefinition = route.Value;
                var parameters = routeDefinition.Handler.Method.GetParameters();
                var args = new object?[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    var param = parameters[i];
                    if (param.ParameterType == typeof(HttpListenerRequest))
                    {
                        args[i] = request;
                    }
                    else if (routeParams.TryGetValue(param.Name!, out var value))
                    {
                        try
                        {
                            args[i] = Convert.ChangeType(value, param.ParameterType, CultureInfo.InvariantCulture);
                        }
                        catch
                        {
                            return Response.BadRequest($"Invalid value for '{param.Name}': '{value}'");
                        }
                    }
                    else
                    {
                        args[i] = GetDefault(param.ParameterType);
                    }
                }

                try
                {
                    var result = routeDefinition.Handler.DynamicInvoke(args);
                    return (Response)result!;
                }
                catch (Exception ex)
                {
                    return Response.BadRequest(ex.InnerException?.Message ?? ex.Message);
                }
            }
        }

        if (routes.TryGetValue((method, "/{*path}"), out var wildcardHandler))
        {
            try
            {
                var result = wildcardHandler.Handler.DynamicInvoke(request);
                return (Response)result!;
            }
            catch (Exception ex)
            {
                return Response.BadRequest(ex.InnerException?.Message ?? ex.Message);
            }
        }

        return new Response
        {
            StatusCode = 404,
            ContentType = "text/plain",
            Body = Encoding.UTF8.GetBytes("Not Found")
        };
    }
    public async Task<Response> RouteAsync(HttpListenerRequest request)
    {
        string method = request.HttpMethod.ToUpperInvariant();
        string path = request.Url!.AbsolutePath;

        foreach (var route in routes)
        {
            var (routeMethod, routePath) = route.Key;
            if (routeMethod != method) continue;

            if (TryMatchRoute(path, routePath, out var routeParams))
            {
                var routeDefinition = route.Value;
                var parameters = routeDefinition.Handler.Method.GetParameters();
                var args = new object?[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    var param = parameters[i];
                    var paramName = param.Name!;
                    var fromRoute = param.GetCustomAttribute<FromRouteAttribute>() != null;
                    var fromQuery = param.GetCustomAttribute<FromQueryAttribute>() != null;
                    var fromBody = param.GetCustomAttribute<FromBodyAttribute>() != null;
                    var fromForm = param.GetCustomAttribute<FromFormAttribute>() != null;

                    if (param.ParameterType == typeof(HttpListenerRequest))
                    {
                        args[i] = request;
                    }
                    else if (fromRoute)
                    {
                        if (routeParams.TryGetValue(paramName, out var value))
                            args[i] = Convert.ChangeType(value, param.ParameterType, CultureInfo.InvariantCulture);
                        else
                            args[i] = GetDefault(param.ParameterType);
                    }
                    else if (fromQuery)
                    {
                        var query = System.Web.HttpUtility.ParseQueryString(request.Url!.Query);

                        if (IsSimpleType(param.ParameterType))
                        {
                            var value = query.Get(paramName);
                            if (value != null)
                                args[i] = Convert.ChangeType(value, param.ParameterType, CultureInfo.InvariantCulture);
                            else
                                args[i] = GetDefault(param.ParameterType);
                        }
                        else if (param.ParameterType.IsClass)
                        {
                            args[i] = query.BindQuery(param.ParameterType);
                        }
                        else
                        {
                            args[i] = GetDefault(param.ParameterType);
                        }

                    }
                    else if (fromForm)
                    {
                        if (param.GetCustomAttribute<FromFormAttribute>() != null)
                        {
                            if (request.ContentType != null && request.ContentType.StartsWith("multipart/form-data"))
                            {
                                args[i] = request.InputStream.BindMultipart(request.ContentType, param.ParameterType);
                            }
                            else
                            {
                                using var reader = new StreamReader(request.InputStream);
                                var body = reader.ReadToEnd();
                                args[i] = RequestBinder.Bind(body, param.ParameterType);
                            }
                        }
                    }
                    else if (fromBody)
                    {
                        using var reader = new StreamReader(request.InputStream);
                        var body = reader.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(body))
                            args[i] = JsonSerializer.Deserialize(body, param.ParameterType, jsonOptions);
                    }
                    else if (IsSimpleType(param.ParameterType))
                    {
                        // fallback to route param if exists
                        if (routeParams.TryGetValue(paramName, out var value))
                            args[i] = Convert.ChangeType(value, param.ParameterType, CultureInfo.InvariantCulture);
                        else
                            args[i] = GetDefault(param.ParameterType);
                    }
                    else if (param.ParameterType.IsClass)
                    {
                        // fallback default: FromBody for complex types if POST/PUT/PATCH
                        if (request.HttpMethod is "POST" or "PUT" or "PATCH")
                        {
                            using var reader = new StreamReader(request.InputStream);
                            var body = reader.ReadToEnd();
                            if (!string.IsNullOrWhiteSpace(body))
                                args[i] = JsonSerializer.Deserialize(body, param.ParameterType, jsonOptions);
                        }
                        else
                        {
                            args[i] = GetDefault(param.ParameterType);
                        }
                    }
                    else
                    {
                        args[i] = GetDefault(param.ParameterType);
                    }
                }

                try
                {
                    var result = routeDefinition.Handler.DynamicInvoke(args);

                    if (result is Task<Response> taskResponse)
                    {
                        return await taskResponse;
                    }
                    else if (result is Response response)
                    {
                        return response;
                    }
                    else
                    {
                        return Response.BadRequest("Handler did not return a Response or Task<Response>.");
                    }
                }
                catch (Exception ex)
                {
                    return Response.BadRequest(ex.InnerException?.Message ?? ex.Message);
                }
            }
        }

        return Response.NotFound();
    }
    private static bool TryMatchRoute(string requestPath, string routePath, out Dictionary<string, string> parameters)
    {
        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var requestParts = requestPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var routeParts = routePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (requestParts.Length != routeParts.Length)
            return false;

        for (int i = 0; i < routeParts.Length; i++)
        {
            if (routeParts[i].StartsWith("{") && routeParts[i].EndsWith("}"))
            {
                var paramName = routeParts[i][1..^1];
                parameters[paramName] = requestParts[i];
            }
            else if (!string.Equals(routeParts[i], requestParts[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }
    private static object? GetDefault(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null;
    private static bool IsSimpleType(Type type)
    {
        return type.IsPrimitive
            || type.IsEnum
            || type.Equals(typeof(string))
            || type.Equals(typeof(Guid))
            || type.Equals(typeof(DateTime))
            || type.Equals(typeof(decimal));
    }
    public void SetMetadata(string method, string path, Action<RouteMetadata> configure)
    {
        var key = (method.ToUpperInvariant(), path);
        if (!_routeMetadata.ContainsKey(key))
            _routeMetadata[key] = new RouteMetadata();

        configure(_routeMetadata[key]);
    }
}