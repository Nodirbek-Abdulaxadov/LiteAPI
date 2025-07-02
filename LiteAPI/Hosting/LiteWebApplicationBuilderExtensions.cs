using LiteAPI.Configurations;

namespace LiteAPI;

public static class LiteWebApplicationBuilderExtensions
{
    public static LiteWebApplicationBuilder Configure<TConfig>(this LiteWebApplicationBuilder builder)
        where TConfig : LiteConfiguration, new()
    {
        var instance = new TConfig();
        instance.Initialize();

        builder.LiteConfiguration = instance;

        // Clear previous registration if any:
        builder.Services.AddSingleton<LiteConfiguration>(instance);

        return builder;
    }
}