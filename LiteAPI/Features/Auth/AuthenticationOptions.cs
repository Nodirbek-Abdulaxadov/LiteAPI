namespace LiteAPI;

public class AuthenticationOptions
{
    public AuthScheme DefaultScheme { get; set; } = AuthScheme.None;
    public string ApiKeyHeader { get; set; } = "X-API-KEY";
    public Func<string, bool>? ValidateApiKey { get; set; }
    public Func<string, bool>? ValidateBearerToken { get; set; }
}

public class AuthorizationOptions
{
    private readonly Dictionary<string, Func<LiteHttpContext, bool>> _policies = [];

    public void AddPolicy(string name, Func<LiteHttpContext, bool> policy)
    {
        _policies[name] = policy;
    }

    public Func<LiteHttpContext, bool>? GetPolicy(string name)
    {
        _policies.TryGetValue(name, out var policy);
        return policy;
    }
}