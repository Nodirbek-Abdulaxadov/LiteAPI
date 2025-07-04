using LiteAPI.Features.Configurations;
using LiteAPI.Features.DependencyInjection;

namespace LiteAPI;

public class LiteWebApplicationBuilder
{
    public Router Router { get; } = new();
    public ServiceCollection Services { get; } = new();
    public LiteConfiguration LiteConfiguration { get; internal set; } = new();

    public LiteWebApplicationBuilder()
    {
        Services.AddSingleton(LiteConfiguration);
    }

    public LiteWebApplicationBuilder Configure(Action<LiteConfiguration> configure)
    {
        configure(LiteConfiguration);
        return this;
    }

    public LiteWebApplication Build()
    {
        LiteConfiguration.Initialize();
        LiteConfiguration.LaunchBrowserIfEnabled();
        // Ensure the correct instance is injected:
        Services.AddSingleton(LiteConfiguration);

        return new LiteWebApplication(Router, Services, LiteConfiguration.Urls);
    }
}