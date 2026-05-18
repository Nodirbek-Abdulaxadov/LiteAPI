using LiteAPI.Routing;

/// <summary>
/// Minimal router for LiteAPI with signature-based routing and shared
/// sync/async invocation path.
/// </summary>
public class Router
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly Dictionary<(string method, string path), RouteDefinition> routes = [];
    private readonly Dictionary<(string method, string path), RouteMetadata> _routeMetadata = [];

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

    /// <summary>Registers a handler only if the route is not already taken (used by MapStaticFiles).</summary>
    internal RouteDefinition? TryHandle(string method, string path, Delegate handler)
    {
        var key = (method.ToUpperInvariant(), path);
        if (routes.ContainsKey(key)) return null;
        var def = new RouteDefinition(key.Item1, path, handler);
        routes[key] = def;
        return def;
    }

    private RouteDefinition Handle(string method, string path, Delegate handler)
    {
        var def = new RouteDefinition(method.ToUpperInvariant(), path, handler);
        routes[(def.Method, def.Path)] = def;
        return def;
    }

    public Response Route(HttpListenerRequest request)
        => RouteAsync(request).GetAwaiter().GetResult();

    public async Task<Response> RouteAsync(HttpListenerRequest request)
    {
        var liteRequest = new LiteAPI.Http.LiteRequest(request);
        var method = liteRequest.Method.ToUpperInvariant();
        var path = liteRequest.Path;

        if (!TryResolve(method, path, out var route, out var routeParams) || route is null)
            return Response.NotFound();

        return await InvokeAsync(route, liteRequest, routeParams);
    }

    internal async Task<Response> InvokeAsync(RouteDefinition routeDefinition, LiteAPI.Http.LiteRequest request, Dictionary<string, string> routeParams, long? maxBodyBytes = null)
    {
        var parameters = routeDefinition.HandlerParameters;
        var args = new object?[parameters.Length];

        // Reject early when more than one parameter wants the body.
        var bodyBoundParamCount = 0;
        foreach (var p in parameters)
        {
            if (NeedsBody(p, request.Method)) bodyBoundParamCount++;
        }
        if (bodyBoundParamCount > 1)
            return Response.BadRequest("Only one body/form parameter is supported per handler.");

        byte[]? bodyBytes = null;
        string? bodyText = null;
        var bodyRead = false;

        if (bodyBoundParamCount == 1)
        {
            try
            {
                using var ms = new MemoryStream();
                if (maxBodyBytes is long limit && limit > 0)
                    await CopyToWithLimitAsync(request.BodyStream, ms, limit);
                else
                    await request.BodyStream.CopyToAsync(ms);

                bodyBytes = ms.ToArray();
                bodyText = Encoding.UTF8.GetString(bodyBytes);
                bodyRead = true;
            }
            catch (PayloadTooLargeException)
            {
                return Response.PayloadTooLarge(maxBodyBytes is long limit
                    ? $"Request body exceeds limit ({limit} bytes)."
                    : "Payload Too Large");
            }
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
                if (request.Raw is null)
                    return Response.InternalServerError("HttpListenerRequest is not available when using the Rust listener. Use LiteRequest instead.");
                args[i] = request.Raw;
                continue;
            }

            if (param.ParameterType == typeof(LiteAPI.Http.LiteRequest))
            {
                args[i] = request;
                continue;
            }

            if (fromRoute || (!fromQuery && !fromBody && !fromForm && IsSimpleType(param.ParameterType) && routeParams.ContainsKey(paramName)))
            {
                if (routeParams.TryGetValue(paramName, out var routeValue))
                {
                    if (!TypeConversion.TryConvert(routeValue, param.ParameterType, out var converted))
                        return Response.BadRequest($"Invalid route value for '{paramName}': '{routeValue}'");
                    args[i] = converted;
                }
                else
                {
                    args[i] = GetDefault(param.ParameterType);
                }
                continue;
            }

            if (fromQuery)
            {
                if (IsSimpleType(param.ParameterType))
                {
                    if (request.Query.TryGetValue(paramName, out var qValue) && qValue != null)
                    {
                        if (!TypeConversion.TryConvert(qValue, param.ParameterType, out var converted))
                            return Response.BadRequest($"Invalid query value for '{paramName}': '{qValue}'");
                        args[i] = converted;
                    }
                    else
                    {
                        args[i] = GetDefault(param.ParameterType);
                    }
                }
                else if (param.ParameterType.IsClass)
                {
                    var query = HttpUtility.ParseQueryString(string.Empty);
                    foreach (var kvp in request.Query)
                        query[kvp.Key] = kvp.Value;
                    args[i] = query.BindQuery(param.ParameterType);
                }
                else
                {
                    args[i] = GetDefault(param.ParameterType);
                }
                continue;
            }

            if (fromForm)
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
                continue;
            }

            if (fromBody)
            {
                args[i] = (bodyRead && !string.IsNullOrWhiteSpace(bodyText))
                    ? DeserializeJsonOrBadRequest(bodyText!, param.ParameterType)
                    : GetDefault(param.ParameterType);
                if (args[i] is Response r) return r;
                continue;
            }

            // No explicit attribute. Heuristic for write methods + complex types.
            if (IsSimpleType(param.ParameterType))
            {
                if (routeParams.TryGetValue(paramName, out var rv) && rv != null)
                {
                    if (!TypeConversion.TryConvert(rv, param.ParameterType, out var converted))
                        return Response.BadRequest($"Invalid value for '{paramName}': '{rv}'");
                    args[i] = converted;
                }
                else if (request.Query.TryGetValue(paramName, out var qv) && qv != null)
                {
                    if (!TypeConversion.TryConvert(qv, param.ParameterType, out var converted))
                        return Response.BadRequest($"Invalid query value for '{paramName}': '{qv}'");
                    args[i] = converted;
                }
                else
                {
                    args[i] = GetDefault(param.ParameterType);
                }
            }
            else if (param.ParameterType.IsClass && request.Method is "POST" or "PUT" or "PATCH")
            {
                args[i] = (bodyRead && !string.IsNullOrWhiteSpace(bodyText))
                    ? DeserializeJsonOrBadRequest(bodyText!, param.ParameterType)
                    : GetDefault(param.ParameterType);
                if (args[i] is Response r) return r;
            }
            else
            {
                args[i] = GetDefault(param.ParameterType);
            }
        }

        // DataAnnotations validation runs on every bound complex argument
        // unless the handler is decorated with [SkipValidation] or validation
        // has been globally disabled via AddValidation(opt => opt.Enabled = false).
        if (ValidationPipeline.IsEnabled
            && routeDefinition.Handler.Method.GetCustomAttribute<SkipValidationAttribute>() is null)
        {
            List<ValidationFailure>? aggregated = null;
            for (int i = 0; i < parameters.Length; i++)
            {
                var value = args[i];
                if (value is null) continue;
                if (!IsComplexBindable(parameters[i].ParameterType)) continue;

                if (!ModelValidator.TryValidate(value, out var failures))
                {
                    aggregated ??= new List<ValidationFailure>();
                    aggregated.AddRange(failures);
                }
            }

            if (aggregated is { Count: > 0 })
            {
                var failureResponse = ValidationPipeline.BuildFailureResponse(aggregated);
                if (failureResponse is not null) return failureResponse;
            }
        }

        try
        {
            var result = routeDefinition.Invoker(args);

            return result switch
            {
                Response response => response,
                Task<Response> taskResponse => await taskResponse.ConfigureAwait(false),
                Task task => await AwaitNonResponseTask(task),
                null => Response.NoContent(),
                _ => Response.BadRequest("Handler did not return a Response or Task<Response>.")
            };
        }
        catch (Exception ex)
        {
            return Response.InternalServerError(ex.InnerException?.Message ?? ex.Message);
        }
    }

    private static bool IsComplexBindable(Type t)
        => t.IsClass
        && t != typeof(string)
        && t != typeof(HttpListenerRequest)
        && t != typeof(LiteAPI.Http.LiteRequest);

    private static async Task<Response> AwaitNonResponseTask(Task task)
    {
        await task.ConfigureAwait(false);
        return Response.NoContent();
    }

    private static object DeserializeJsonOrBadRequest(string json, Type type)
    {
        try
        {
            return JsonSerializer.Deserialize(json, type, _jsonOptions) ?? GetDefault(type)!;
        }
        catch (JsonException ex)
        {
            return Response.BadRequest($"Invalid JSON body: {ex.Message}");
        }
    }

    private static bool NeedsBody(ParameterInfo p, string requestMethod)
    {
        if (p.GetCustomAttribute<FromBodyAttribute>() != null) return true;
        if (p.GetCustomAttribute<FromFormAttribute>() != null) return true;
        if (p.GetCustomAttribute<FromRouteAttribute>() != null) return false;
        if (p.GetCustomAttribute<FromQueryAttribute>() != null) return false;
        if (p.ParameterType == typeof(HttpListenerRequest)) return false;
        if (p.ParameterType == typeof(LiteAPI.Http.LiteRequest)) return false;
        if (!IsSimpleType(p.ParameterType) && p.ParameterType.IsClass && requestMethod is "POST" or "PUT" or "PATCH")
            return true;
        return false;
    }

    private sealed class PayloadTooLargeException : Exception { }

    private static async Task CopyToWithLimitAsync(Stream input, Stream output, long maxBytes)
    {
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer);
            if (read <= 0)
                break;

            total += read;
            if (total > maxBytes)
                throw new PayloadTooLargeException();

            await output.WriteAsync(buffer.AsMemory(0, read));
        }
    }

    /// <summary>
    /// Invokes a route by raw method/path/body, honouring optional headers and
    /// content-type so request binding behaves the same as the HTTP path.
    /// </summary>
    public Response HandleRawRequest(string method, string path, string? body, IDictionary<string, string>? headers = null, string? contentType = null)
    {
        method = method.ToUpperInvariant();

        var pathOnly = path;
        var queryDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var qIndex = path.IndexOf('?', StringComparison.Ordinal);
        if (qIndex >= 0)
        {
            pathOnly = path[..qIndex];
            var parsed = HttpUtility.ParseQueryString(path[(qIndex + 1)..]);
            foreach (string? key in parsed.AllKeys!)
            {
                if (key != null)
                    queryDict[key] = parsed[key]!;
            }
        }

        if (!TryResolve(method, pathOnly, out var routeDefinition, out var routeParams) || routeDefinition is null)
            return Response.NotFound();

        var bodyBytes = string.IsNullOrEmpty(body) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(body);
        var headerDict = headers is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);

        var effectiveContentType = contentType
            ?? (headerDict.TryGetValue("Content-Type", out var ct) ? ct : null)
            ?? (LooksLikeJson(body) ? "application/json; charset=utf-8" : "text/plain; charset=utf-8");

        var request = new LiteAPI.Http.LiteRequest(
            method,
            pathOnly,
            headers: headerDict,
            query: queryDict,
            bodyStream: new MemoryStream(bodyBytes, writable: false),
            contentLength: bodyBytes.Length,
            contentType: effectiveContentType,
            remoteIp: null);

        return InvokeAsync(routeDefinition, request, routeParams).GetAwaiter().GetResult();
    }

    private static bool LooksLikeJson(string? body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        var c = body.TrimStart().FirstOrDefault();
        return c == '{' || c == '[';
    }

    private static bool TryMatchRoute(string requestPath, string routePath, out Dictionary<string, string> parameters)
    {
        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var requestParts = requestPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var routeParts = routePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (routeParts.Length == 0)
            return requestParts.Length == 0;

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

            if (routePart.StartsWith('{') && routePart.EndsWith('}'))
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
            if (part.StartsWith('{') && part.EndsWith('}'))
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
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(Guid)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(DateOnly)
            || underlying == typeof(TimeOnly)
            || underlying == typeof(decimal);
    }

    public void SetMetadata(string method, string path, Action<RouteMetadata> configure)
    {
        var key = (method.ToUpperInvariant(), path);
        if (!_routeMetadata.ContainsKey(key))
            _routeMetadata[key] = new RouteMetadata();

        configure(_routeMetadata[key]);

        if (routes.TryGetValue(key, out var route))
            configure(route.Metadata);
    }
}
