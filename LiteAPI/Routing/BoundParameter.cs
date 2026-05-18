namespace LiteAPI.Routing;

/// <summary>
/// Per-parameter binding metadata, computed once at route registration time
/// so the hot path doesn't pay for <c>ParameterInfo.GetCustomAttribute&lt;&gt;()</c>
/// on every request.
/// </summary>
internal sealed class BoundParameter
{
    public ParameterInfo Info { get; }
    public string Name { get; }
    public Type Type { get; }

    public bool FromRoute { get; }
    public bool FromQuery { get; }
    public bool FromBody { get; }
    public bool FromForm { get; }

    public bool IsHttpListenerRequest { get; }
    public bool IsLiteRequest { get; }
    public bool IsSimple { get; }

    public BoundParameter(ParameterInfo p)
    {
        Info = p;
        Name = p.Name ?? string.Empty;
        Type = p.ParameterType;

        FromRoute = p.GetCustomAttribute<FromRouteAttribute>() != null;
        FromQuery = p.GetCustomAttribute<FromQueryAttribute>() != null;
        FromBody  = p.GetCustomAttribute<FromBodyAttribute>()  != null;
        FromForm  = p.GetCustomAttribute<FromFormAttribute>()  != null;

        IsHttpListenerRequest = Type == typeof(HttpListenerRequest);
        IsLiteRequest         = Type == typeof(LiteAPI.Http.LiteRequest);
        IsSimple              = IsSimpleType(Type);
    }

    /// <summary>
    /// True when this parameter wants the request body (so the router needs
    /// to read it once before binding the parameters).
    /// </summary>
    public bool NeedsBody(string requestMethod)
    {
        if (FromBody || FromForm) return true;
        if (FromRoute || FromQuery) return false;
        if (IsHttpListenerRequest || IsLiteRequest) return false;
        return !IsSimple
            && Type.IsClass
            && (requestMethod == "POST" || requestMethod == "PUT" || requestMethod == "PATCH");
    }

    public static BoundParameter[] Build(ParameterInfo[] parameters)
    {
        if (parameters.Length == 0) return Array.Empty<BoundParameter>();
        var arr = new BoundParameter[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
            arr[i] = new BoundParameter(parameters[i]);
        return arr;
    }

    private static bool IsSimpleType(Type t)
    {
        var underlying = Nullable.GetUnderlyingType(t) ?? t;
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
}
