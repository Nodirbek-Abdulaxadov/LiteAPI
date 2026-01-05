public class LiteServerOptions
{
    /// <summary>
    /// Maximum number of requests processed concurrently.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 256;

    /// <summary>
    /// Maximum allowed request body size in bytes. If exceeded, returns 413.
    /// When null, no limit is enforced.
    /// </summary>
    public long? MaxRequestBodyBytes { get; set; } = 10 * 1024 * 1024;
}
