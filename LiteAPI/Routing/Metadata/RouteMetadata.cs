public class RouteMetadata
{
    public bool AllowAnonymous { get; set; }
    public string? RequiredPolicy { get; set; }
    public List<string> RequiredRoles { get; set; } = [];

    public void SetAllowAnonymous() => AllowAnonymous = true;
    public void SetRequiredPolicy(string policy) => RequiredPolicy = policy;
    public void AddRequiredRole(params string[] roles)
    {
        RequiredRoles.AddRange(roles);
    }
}