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

    internal bool TryResolve(string method, string path, out RouteDefinition? route, out Dictionary<string, string> routeParams)
    {
        method = method.ToUpperInvariant();

        route = null;
        routeParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        int bestScore = int.MinValue;
        Dictionary<string, string>? bestParams = null;

        foreach (var kvp in routes)
        {
            var (routeMethod, routePath) = kvp.Key;
            if (!string.Equals(routeMethod, method, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryMatchRoute(path, routePath, out var parameters))
                continue;

            var score = ComputeSpecificityScore(routePath);
            if (score <= bestScore)
                continue;

            bestScore = score;
            route = kvp.Value;
            bestParams = parameters;
        }

        if (route is null)
            return false;

        routeParams = bestParams ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return true;
    }

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
        var method = request.HttpMethod.ToUpperInvariant();
        var path = request.Url!.AbsolutePath;

        if (!TryResolve(method, path, out var route, out var routeParams) || route is null)
            return Response.NotFound();

        return InvokeSync(route, request, routeParams);
    }
    public async Task<Response> RouteAsync(HttpListenerRequest request)
    {
        var method = request.HttpMethod.ToUpperInvariant();
        var path = request.Url!.AbsolutePath;

        if (!TryResolve(method, path, out var route, out var routeParams) || route is null)
            return Response.NotFound();

        return await InvokeAsync(route, request, routeParams);
    }

    internal async Task<Response> InvokeAsync(RouteDefinition routeDefinition, HttpListenerRequest request, Dictionary<string, string> routeParams)
    {
        var parameters = routeDefinition.Handler.Method.GetParameters();
        var args = new object?[parameters.Length];

        // Read body once (if needed)
        byte[]? bodyBytes = null;
        string? bodyText = null;
        bool bodyRead = false;
        int bodyBoundParamCount = 0;

        bool NeedsBodyFor(ParameterInfo p)
        {
            if (p.GetCustomAttribute<FromBodyAttribute>() != null)
                return true;

            if (p.GetCustomAttribute<FromFormAttribute>() != null)
                return true;

            // fallback: complex types on write methods
            if (!IsSimpleType(p.ParameterType) && p.ParameterType.IsClass && request.HttpMethod is "POST" or "PUT" or "PATCH")
                return true;

            return false;
        }

        if (parameters.Any(NeedsBodyFor))
        {
            using var ms = new MemoryStream();
            await request.InputStream.CopyToAsync(ms);
            bodyBytes = ms.ToArray();
            bodyText = Encoding.UTF8.GetString(bodyBytes);
            bodyRead = true;

            bodyBoundParamCount = parameters.Count(p =>
                p.GetCustomAttribute<FromBodyAttribute>() != null
                || p.GetCustomAttribute<FromFormAttribute>() != null
                || (!IsSimpleType(p.ParameterType) && p.ParameterType.IsClass && request.HttpMethod is "POST" or "PUT" or "PATCH"));

            if (bodyBoundParamCount > 1)
                return Response.BadRequest("Only one body/form parameter is supported per handler.");
        }

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
                if (!bodyRead || bodyBytes is null)
                {
                    args[i] = GetDefault(param.ParameterType);
                }
                else if (request.ContentType != null && request.ContentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = new MemoryStream(bodyBytes, writable: false);
                    args[i] = stream.BindMultipart(request.ContentType, param.ParameterType);
                }
                else
                {
                    args[i] = RequestBinder.Bind(bodyText ?? string.Empty, param.ParameterType);
                }
            }
            else if (fromBody)
            {
                if (!bodyRead || string.IsNullOrWhiteSpace(bodyText))
                {
                    args[i] = GetDefault(param.ParameterType);
                }
                else
                {
                    args[i] = JsonSerializer.Deserialize(bodyText, param.ParameterType, jsonOptions);
                }
            }
            else if (IsSimpleType(param.ParameterType))
            {
                if (routeParams.TryGetValue(paramName, out var value))
                    args[i] = Convert.ChangeType(value, param.ParameterType, CultureInfo.InvariantCulture);
                else
                    args[i] = GetDefault(param.ParameterType);
            }
            else if (param.ParameterType.IsClass)
            {
                if (request.HttpMethod is "POST" or "PUT" or "PATCH")
                {
                    if (!bodyRead || string.IsNullOrWhiteSpace(bodyText))
                        args[i] = GetDefault(param.ParameterType);
                    else
                        args[i] = JsonSerializer.Deserialize(bodyText, param.ParameterType, jsonOptions);
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
                return await taskResponse;

            if (result is Response response)
                return response;

            return Response.BadRequest("Handler did not return a Response or Task<Response>.");
        }
        catch (Exception ex)
        {
            // treat handler exceptions as 500, not 400
            return Response.InternalServerError(ex.InnerException?.Message ?? ex.Message);
        }
    }

    internal Response InvokeSync(RouteDefinition routeDefinition, HttpListenerRequest request, Dictionary<string, string> routeParams)
    {
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
            return Response.InternalServerError(ex.InnerException?.Message ?? ex.Message);
        }
    }

    public Response HandleRawRequest(string method, string path, string? body)
    {
        method = method.ToUpperInvariant();

        if (!TryResolve(method, path, out var routeDefinition, out var routeParams) || routeDefinition is null)
            return Response.NotFound();

        var parameters = routeDefinition.Handler.Method.GetParameters();
        var args = new object?[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    var param = parameters[i];
                    var paramName = param.Name!;

                    if (param.ParameterType == typeof(string) && parameters.Length == 1 && routeParams.Count == 0)
                    {
                        // Body ni string sifatida berish
                        args[i] = body;
                    }
                    else if (routeParams.TryGetValue(paramName, out var value))
                    {
                        args[i] = Convert.ChangeType(value, param.ParameterType, CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        args[i] = GetDefault(param.ParameterType);
                    }
                }

        try
        {
            var result = routeDefinition.Handler.DynamicInvoke(args);

            if (result is Response r)
                return r;

            if (result is string s)
                return Response.Ok(s); // avtomatik o‘rash

            if (result is Task<Response> taskResp)
                return taskResp.GetAwaiter().GetResult();

            return Response.BadRequest("Handler did not return a valid Response");
        }
        catch (Exception ex)
        {
            return Response.InternalServerError(ex.InnerException?.Message ?? ex.Message);
        }
    }
    private static bool TryMatchRoute(string requestPath, string routePath, out Dictionary<string, string> parameters)
    {
        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var requestParts = requestPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var routeParts = routePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (routeParts.Length == 0)
            return requestParts.Length == 0;

        // Support a single trailing wildcard segment: "{*path}"
        var hasTrailingWildcard = routeParts.Length > 0
            && routeParts[^1].StartsWith("{*", StringComparison.Ordinal)
            && routeParts[^1].EndsWith("}", StringComparison.Ordinal);

        if (!hasTrailingWildcard && requestParts.Length != routeParts.Length)
            return false;

        if (hasTrailingWildcard && requestParts.Length < routeParts.Length - 1)
            return false;

        for (int i = 0; i < routeParts.Length; i++)
        {
            var routePart = routeParts[i];

            if (routePart.StartsWith("{*", StringComparison.Ordinal) && routePart.EndsWith("}", StringComparison.Ordinal))
            {
                if (i != routeParts.Length - 1)
                    return false;

                var paramName = routePart[2..^1];
                var remainder = requestParts.Length <= i
                    ? string.Empty
                    : string.Join('/', requestParts.Skip(i));
                parameters[paramName] = remainder;
                return true;
            }
            if (i >= requestParts.Length)
                return false;

            if (routePart.StartsWith("{") && routePart.EndsWith("}"))
            {
                var paramName = routePart[1..^1];
                parameters[paramName] = requestParts[i];
            }
            else if (!string.Equals(routePart, requestParts[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private static int ComputeSpecificityScore(string routePath)
    {
        // Higher score = more specific
        // exact literals > parameters > wildcard
        var parts = routePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return 1_000_000;

        var hasWildcard = parts.Length > 0 && parts[^1].StartsWith("{*", StringComparison.Ordinal) && parts[^1].EndsWith("}", StringComparison.Ordinal);
        int literalCount = 0;
        int paramCount = 0;

        foreach (var part in parts)
        {
            if (part.StartsWith("{*", StringComparison.Ordinal) && part.EndsWith("}", StringComparison.Ordinal))
                continue;
            if (part.StartsWith("{", StringComparison.Ordinal) && part.EndsWith("}", StringComparison.Ordinal))
                paramCount++;
            else
                literalCount++;
        }

        var score = literalCount * 100 - paramCount * 10 + parts.Length;
        if (hasWildcard)
            score -= 10_000;

        return score;
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

        // Keep RouteDefinition.Metadata as the single source of truth when possible
        if (routes.TryGetValue(key, out var route))
        {
            configure(route.Metadata);
        }
    }
}