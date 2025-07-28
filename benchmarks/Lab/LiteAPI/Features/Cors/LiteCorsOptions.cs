public class LiteCorsOptions
{
    public HashSet<string> AllowedOrigins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AllowAnyOrigin { get; set; } = false;
    public bool AllowCredentials { get; set; } = false;
    public string AllowedHeaders { get; set; } = "Content-Type, Authorization";
    public string AllowedMethods { get; set; } = "GET, POST, PUT, DELETE, OPTIONS";
}