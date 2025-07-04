namespace LiteAPI;

public class RouteDefinition(string method, string path, Delegate handler)
{
    public string Path { get; } = path;
    public string Method { get; } = method;
    public Delegate Handler { get; } = handler;
    public RouteMetadata Metadata { get; } = new();

    public RouteDefinition WithMetadata(Action<RouteMetadata> configure)
    {
        configure(Metadata);
        return this;
    }
}