public static class RouteAuthorizationExtensions
{
    public static void AllowAnonymous(this Delegate handler, LiteWebApplication app, string path, string method)
    {
        app.Router.SetMetadata(method, path, meta => meta.AllowAnonymous = true);
    }

    public static void RequirePolicy(this Delegate handler, LiteWebApplication app, string path, string method, string policyName)
    {
        app.Router.SetMetadata(method, path, meta => meta.RequiredPolicy = policyName);
    }

    public static void RequireRoles(this Delegate handler, LiteWebApplication app, string path, string method, params string[] roles)
    {
        app.Router.SetMetadata(method, path, meta => meta.RequiredRoles.AddRange(roles));
    }
}