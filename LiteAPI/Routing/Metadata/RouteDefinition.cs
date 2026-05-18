using LiteAPI.Routing;

public class RouteDefinition
{
    public string Path { get; }
    public string Method { get; }
    public Delegate Handler { get; }
    public RouteMetadata Metadata { get; } = new();

    /// <summary>Parameters of <see cref="Handler"/>'s method, cached once.</summary>
    internal ParameterInfo[] HandlerParameters { get; }

    /// <summary>Compiled fast invoker for <see cref="Handler"/>; avoids per-request <see cref="Delegate.DynamicInvoke"/>.</summary>
    internal Func<object?[], object?> Invoker { get; }

    public RouteDefinition(string method, string path, Delegate handler)
    {
        Method = method;
        Path = path;
        Handler = handler;
        HandlerParameters = handler.Method.GetParameters();
        Invoker = DelegateInvoker.Compile(handler);
    }

    public RouteDefinition WithMetadata(Action<RouteMetadata> configure)
    {
        configure(Metadata);
        return this;
    }
}
