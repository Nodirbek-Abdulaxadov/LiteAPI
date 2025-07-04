using LiteAPI.Features.Configurations;
using LiteAPI.Features.DependencyInjection;

namespace LiteAPI;

public class LiteWebApplicationBuilder
{
    public Router Router { get; } = new();
    public ServiceCollection Services { get; } = new();
    public LiteConfiguration LiteConfiguration { get; internal set; } = new();
    internal AuthenticationOptions AuthOptions { get; } = new();
    internal AuthorizationOptions AuthorizationOptions { get; } = new();

    public LiteWebApplicationBuilder()
    {
        Services.AddSingleton(LiteConfiguration);
    }

    public LiteWebApplicationBuilder Configure(Action<LiteConfiguration> configure)
    {
        configure(LiteConfiguration);
        return this;
    }

    public LiteWebApplicationBuilder AddAuthentication(Action<AuthenticationOptions> configure)
    {
        configure(AuthOptions);
        return this;
    }

    public LiteWebApplicationBuilder AddAuthorization(Action<AuthorizationOptions> configure)
    {
        configure(AuthorizationOptions);
        return this;
    }

    public LiteWebApplication Build()
    {
        LiteConfiguration.Initialize();
        LiteConfiguration.LaunchBrowserIfEnabled();
        Services.AddSingleton(LiteConfiguration);

        return new LiteWebApplication(Router, Services, LiteConfiguration.Urls, AuthOptions, AuthorizationOptions);
    }
}