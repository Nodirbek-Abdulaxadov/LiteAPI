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

    // Public-shape registration store. Kept to preserve the GetRoutes() contract.
    private readonly Dictionary<(string method, string path), RouteDefinition> routes = [];

    // Hot-path indices, derived from `routes`.
    // _staticRoutes: O(1) lookup when no '{' appears in the path.
    // _paramRoutes:  iterated in registration order for path-template matching.
    private readonly Dictionary<(string method, string path), RouteDefinition> _staticRoutes = [];
    private readonly List<RouteDefinition> _paramRoutes = [];

    private readonly Dictionary<(string method, string path), RouteMetadata> _routeMetadata = [];

    // Re-used between requests when a route has no captures (common static case).
    private static readonly Dictionary<string, string> _emptyParams = new(StringComparer.OrdinalIgnoreCase);

    internal bool TryResolve(string method, string path, out RouteDefinition? route, out Dictionary<string, string> routeParams)
    {
        method = method.ToUpperInvariant();

        // Fast path: literal routes are 99% of traffic. Avoid the foreach loop.
        if (_staticRoutes.TryGetValue((method, path), out var fast))
        {
            route = fast;
            routeParams = _emptyParams;
            return true;
        }

        route = null;
        routeParams = _emptyParams;

        int bestScore = int.MinValue;
        Dictionary<string, string>? bestParams = null;
        RouteDefinition? bestRoute = null;

        // Slow path: walk param/wildcard routes.
        for (int i = 0; i < _paramRoutes.Count; i++)
        {
            var candidate = _paramRoutes[i];
            if (candidate.Method != method) continue;

            if (!TryMatchSegments(path, candidate.PathSegments, candidate.HasTrailingWildcard, out var parameters))
                continue;

            var score = candidate.SpecificityScore;
            if (score <= bestScore) continue;

            bestScore = score;
            bestRoute = candidate;
            bestParams = parameters;
        }

        if (bestRoute is null) return false;

        route = bestRoute;
        routeParams = bestParams ?? _emptyParams;
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
        AddRoute(def);
        return def;
    }

    private RouteDefinition Handle(string method, string path, Delegate handler)
    {
        var def = new RouteDefinition(method.ToUpperInvariant(), path, handler);
        AddRoute(def);
        return def;
    }

    private void AddRoute(RouteDefinition def)
    {
        var key = (def.Method, def.Path);
        // Replace semantics: re-registering the same key wipes the old entry from
        // whichever bucket holds it.
        if (routes.TryGetValue(key, out var existing))
        {
            if (existing.HasRouteParameters) _paramRoutes.Remove(existing);
            else _staticRoutes.Remove(key);
        }

        routes[key] = def;
        if (def.HasRouteParameters)
            _paramRoutes.Add(def);
        else
            _staticRoutes[key] = def;
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
        var parameters = routeDefinition.BoundParameters;
        var args = new object?[parameters.Length];

        // Single-pass: tally body-bound params and reject early on the second one.
        var requestMethod = request.Method;
        var bodyBoundParamCount = 0;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].NeedsBody(requestMethod))
            {
                bodyBoundParamCount++;
                if (bodyBoundParamCount > 1)
                    return Response.BadRequest("Only one body/form parameter is supported per handler.");
            }
        }

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
            var paramName = param.Name;

            if (param.IsHttpListenerRequest)
            {
                if (request.Raw is null)
                    return Response.InternalServerError("HttpListenerRequest is not available when using the Rust listener. Use LiteRequest instead.");
                args[i] = request.Raw;
                continue;
            }

            if (param.IsLiteRequest)
            {
                args[i] = request;
                continue;
            }

            if (param.FromRoute || (!param.FromQuery && !param.FromBody && !param.FromForm && param.IsSimple && routeParams.ContainsKey(paramName)))
            {
                if (routeParams.TryGetValue(paramName, out var routeValue))
                {
                    if (!TypeConversion.TryConvert(routeValue, param.Type, out var converted))
                        return Response.BadRequest($"Invalid route value for '{paramName}': '{routeValue}'");
                    args[i] = converted;
                }
                else
                {
                    args[i] = GetDefault(param.Type);
                }
                continue;
            }

            if (param.FromQuery)
            {
                if (param.IsSimple)
                {
                    if (request.Query.TryGetValue(paramName, out var qValue) && qValue != null)
                    {
                        if (!TypeConversion.TryConvert(qValue, param.Type, out var converted))
                            return Response.BadRequest($"Invalid query value for '{paramName}': '{qValue}'");
                        args[i] = converted;
                    }
                    else
                    {
                        args[i] = GetDefault(param.Type);
                    }
                }
                else if (param.Type.IsClass)
                {
                    var query = HttpUtility.ParseQueryString(string.Empty);
                    foreach (var kvp in request.Query)
                        query[kvp.Key] = kvp.Value;
                    args[i] = query.BindQuery(param.Type);
                }
                else
                {
                    args[i] = GetDefault(param.Type);
                }
                continue;
            }

            if (param.FromForm)
            {
                if (!bodyRead || bodyBytes is null)
                {
                    args[i] = GetDefault(param.Type);
                }
                else if (request.ContentType != null && request.ContentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = new MemoryStream(bodyBytes, writable: false);
                    args[i] = stream.BindMultipart(request.ContentType, param.Type);
                }
                else
                {
                    args[i] = RequestBinder.Bind(bodyText ?? string.Empty, param.Type);
                }
                continue;
            }

            if (param.FromBody)
            {
                args[i] = (bodyRead && !string.IsNullOrWhiteSpace(bodyText))
                    ? DeserializeJsonOrBadRequest(bodyText!, param.Type)
                    : GetDefault(param.Type);
                if (args[i] is Response r) return r;
                continue;
            }

            // No explicit attribute. Heuristic for write methods + complex types.
            if (param.IsSimple)
            {
                if (routeParams.TryGetValue(paramName, out var rv) && rv != null)
                {
                    if (!TypeConversion.TryConvert(rv, param.Type, out var converted))
                        return Response.BadRequest($"Invalid value for '{paramName}': '{rv}'");
                    args[i] = converted;
                }
                else if (request.Query.TryGetValue(paramName, out var qv) && qv != null)
                {
                    if (!TypeConversion.TryConvert(qv, param.Type, out var converted))
                        return Response.BadRequest($"Invalid query value for '{paramName}': '{qv}'");
                    args[i] = converted;
                }
                else
                {
                    args[i] = GetDefault(param.Type);
                }
            }
            else if (param.Type.IsClass && (requestMethod == "POST" || requestMethod == "PUT" || requestMethod == "PATCH"))
            {
                args[i] = (bodyRead && !string.IsNullOrWhiteSpace(bodyText))
                    ? DeserializeJsonOrBadRequest(bodyText!, param.Type)
                    : GetDefault(param.Type);
                if (args[i] is Response r) return r;
            }
            else
            {
                args[i] = GetDefault(param.Type);
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
                if (!IsComplexBindable(parameters[i].Type)) continue;

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

    private sealed class PayloadTooLargeException : Exception { }

    private static async Task CopyToWithLimitAsync(Stream input, Stream output, long maxBytes)
    {
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer);
            if (read <= 0) break;

            total += read;
            if (total > maxBytes) throw new PayloadTooLargeException();

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

    /// <summary>
    /// Span-based segment matcher. Iterates the request path once without
    /// allocating an intermediate <c>string[]</c> for the request side.
    /// </summary>
    private static bool TryMatchSegments(string requestPath, string[] routeSegments, bool hasTrailingWildcard, out Dictionary<string, string> parameters)
    {
        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = requestPath.AsSpan();

        // Skip leading slashes.
        int p = 0;
        while (p < path.Length && path[p] == '/') p++;

        if (p == path.Length)
            return routeSegments.Length == 0;

        if (routeSegments.Length == 0) return false;

        int routeIdx = 0;
        while (p < path.Length)
        {
            // Find end of this request segment.
            int segStart = p;
            while (p < path.Length && path[p] != '/') p++;
            var segment = path[segStart..p];

            if (routeIdx >= routeSegments.Length)
                return false;

            var routePart = routeSegments[routeIdx];

            // Trailing wildcard captures everything from here on.
            if (routePart.Length >= 3 && routePart[0] == '{' && routePart[1] == '*' && routePart[^1] == '}')
            {
                if (routeIdx != routeSegments.Length - 1)
                    return false;
                var paramName = routePart[2..^1];
                parameters[paramName] = path[segStart..].ToString().TrimEnd('/');
                return true;
            }

            if (routePart.Length >= 2 && routePart[0] == '{' && routePart[^1] == '}')
            {
                var paramName = routePart[1..^1];
                parameters[paramName] = segment.ToString();
            }
            else if (!segment.Equals(routePart.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            routeIdx++;

            // Skip the separator slash(es).
            while (p < path.Length && path[p] == '/') p++;
        }

        if (routeIdx < routeSegments.Length)
        {
            // Allow trailing wildcard to absorb zero segments.
            if (hasTrailingWildcard && routeIdx == routeSegments.Length - 1)
            {
                var routePart = routeSegments[routeIdx];
                if (routePart.Length >= 3 && routePart[0] == '{' && routePart[1] == '*' && routePart[^1] == '}')
                {
                    parameters[routePart[2..^1]] = string.Empty;
                    return true;
                }
            }
            return false;
        }

        return true;
    }

    private static object? GetDefault(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null;

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
