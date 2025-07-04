public static class RouteDefinitionExtensions
{
    public static RouteDefinition AllowAnonymous(this RouteDefinition route)
    {
        route.Metadata.SetAllowAnonymous();
        return route;
    }

    public static RouteDefinition RequirePolicy(this RouteDefinition route, string policy)
    {
        route.Metadata.SetRequiredPolicy(policy);
        return route;
    }

    public static RouteDefinition RequireRoles(this RouteDefinition route, params string[] roles)
    {
        route.Metadata.AddRequiredRole(roles);
        return route;
    }
}