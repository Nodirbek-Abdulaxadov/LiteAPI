using LiteAPI.Routing;

public class RouteDefinition
{
    public string Path { get; }
    public string Method { get; }
    public Delegate Handler { get; }
    public RouteMetadata Metadata { get; } = new();

    /// <summary>Parameters of <see cref="Handler"/>'s method, cached once.</summary>
    internal ParameterInfo[] HandlerParameters { get; }

    /// <summary>Per-parameter binding metadata (attributes, type kind), cached.</summary>
    internal BoundParameter[] BoundParameters { get; }

    /// <summary>Compiled fast invoker for <see cref="Handler"/>; avoids per-request <see cref="Delegate.DynamicInvoke"/>.</summary>
    internal Func<object?[], object?> Invoker { get; }

    /// <summary>Pre-split path segments; avoids <c>string.Split</c> in <c>TryMatchRoute</c>.</summary>
    internal string[] PathSegments { get; }

    /// <summary>True when at least one path segment is a <c>{param}</c> or <c>{*wildcard}</c>.</summary>
    internal bool HasRouteParameters { get; }

    internal bool HasTrailingWildcard { get; }

    /// <summary>Pre-computed specificity score; used to break ties when multiple routes match.</summary>
    internal int SpecificityScore { get; }

    public RouteDefinition(string method, string path, Delegate handler)
    {
        Method = method;
        Path = path;
        Handler = handler;
        HandlerParameters = handler.Method.GetParameters();
        BoundParameters = BoundParameter.Build(HandlerParameters);
        Invoker = DelegateInvoker.Compile(handler);

        PathSegments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        var hasParams = false;
        var hasTrailingWildcard = false;
        foreach (var part in PathSegments)
        {
            if (part.Length >= 2 && part[0] == '{' && part[^1] == '}')
            {
                hasParams = true;
                if (part.Length >= 3 && part[1] == '*')
                    hasTrailingWildcard = true;
            }
        }

        HasRouteParameters = hasParams;
        HasTrailingWildcard = hasTrailingWildcard;
        SpecificityScore = ComputeSpecificityScore(PathSegments, hasTrailingWildcard);
    }

    public RouteDefinition WithMetadata(Action<RouteMetadata> configure)
    {
        configure(Metadata);
        return this;
    }

    private static int ComputeSpecificityScore(string[] parts, bool hasWildcard)
    {
        if (parts.Length == 0) return 1_000_000;

        int literalCount = 0;
        int paramCount = 0;

        foreach (var part in parts)
        {
            if (part.Length >= 3 && part[0] == '{' && part[1] == '*' && part[^1] == '}')
                continue;
            if (part.Length >= 2 && part[0] == '{' && part[^1] == '}')
                paramCount++;
            else
                literalCount++;
        }

        var score = literalCount * 100 - paramCount * 10 + parts.Length;
        if (hasWildcard) score -= 10_000;
        return score;
    }
}
