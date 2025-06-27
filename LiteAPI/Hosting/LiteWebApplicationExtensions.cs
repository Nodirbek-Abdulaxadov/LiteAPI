namespace LiteAPI;

public static class LiteWebApplicationExtensions
{
    public static void MapGroup(this LiteWebApplication app, string prefix, Action<LiteWebApplicationGroup> configure)
    {
        var group = new LiteWebApplicationGroup(app.Router, prefix);
        configure(group);
    }
}