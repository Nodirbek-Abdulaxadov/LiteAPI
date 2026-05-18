public static class ValidationExtensions
{
    /// <summary>
    /// Registers <see cref="ValidationOptions"/> in the DI container and publishes them
    /// onto <c>ValidationPipeline.GlobalOptions</c> so the router can consume them
    /// without taking a DI dependency on the request hot path.
    /// </summary>
    public static LiteWebApplicationBuilder AddValidation(
        this LiteWebApplicationBuilder builder,
        Action<ValidationOptions>? configure = null)
    {
        var options = new ValidationOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);
        ValidationPipeline.GlobalOptions = options;
        return builder;
    }
}
