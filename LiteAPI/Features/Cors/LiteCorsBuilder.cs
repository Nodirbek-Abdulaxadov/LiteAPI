public class LiteCorsBuilder
{
    internal HashSet<string> AllowedOrigins { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal string AllowedMethods { get; private set; } = "GET, POST, PUT, DELETE, OPTIONS";
    internal string AllowedHeaders { get; private set; } = "Content-Type, Authorization";
    internal bool AllowAnyOriginFlag { get; private set; } = false;
    internal bool AllowCredentialsFlag { get; private set; } = false;

    public LiteCorsBuilder AllowMethods(params string[] methods)
    {
        AllowedMethods = string.Join(", ", methods);
        return this;
    }

    public LiteCorsBuilder AllowHeaders(params string[] headers)
    {
        AllowedHeaders = string.Join(", ", headers);
        return this;
    }

    public LiteCorsBuilder AllowAnyOrigin()
    {
        AllowAnyOriginFlag = true;
        return this;
    }

    public LiteCorsBuilder AllowCredentials()
    {
        AllowCredentialsFlag = true;
        return this;
    }
}