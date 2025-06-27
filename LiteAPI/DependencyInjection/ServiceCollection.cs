namespace LiteAPI;

public class ServiceCollection
{
    private readonly Dictionary<Type, ServiceDescriptor> _descriptors = [];
    private readonly Dictionary<Type, object> _singletonInstances = [];

    public void AddSingleton<TService, TImplementation>() where TImplementation : TService, new()
    {
        _descriptors[typeof(TService)] = new ServiceDescriptor(typeof(TService), typeof(TImplementation), ServiceLifetime.Singleton);
    }

    public void AddSingleton<TService>(TService implementation) where TService : class
    {
        _descriptors[typeof(TService)] = new ServiceDescriptor(typeof(TService), implementation);
        _singletonInstances[typeof(TService)] = implementation;
    }

    public void AddScoped<TService, TImplementation>() where TImplementation : TService, new()
    {
        _descriptors[typeof(TService)] = new ServiceDescriptor(typeof(TService), typeof(TImplementation), ServiceLifetime.Scoped);
    }

    public void AddTransient<TService, TImplementation>() where TImplementation : TService, new()
    {
        _descriptors[typeof(TService)] = new ServiceDescriptor(typeof(TService), typeof(TImplementation), ServiceLifetime.Transient);
    }

    public void AddTransient<TService>() where TService : class, new()
    {
        _descriptors[typeof(TService)] = new ServiceDescriptor(typeof(TService), typeof(TService), ServiceLifetime.Transient);
    }

    public void AddScoped<TService>() where TService : class, new()
    {
        _descriptors[typeof(TService)] = new ServiceDescriptor(typeof(TService), typeof(TService), ServiceLifetime.Scoped);
    }

    public void AddSingleton<TService>() where TService : class, new()
    {
        _descriptors[typeof(TService)] = new ServiceDescriptor(typeof(TService), typeof(TService), ServiceLifetime.Singleton);
    }

    internal object Resolve(Type serviceType, Dictionary<Type, object> scopedInstances)
    {
        if (!_descriptors.TryGetValue(serviceType, out var descriptor))
        {
            throw new InvalidOperationException($"Service {serviceType.Name} not registered.");
        }

        return descriptor.Lifetime switch
        {
            ServiceLifetime.Singleton => GetOrCreateSingleton(descriptor),
            ServiceLifetime.Scoped => GetOrCreateScoped(descriptor, scopedInstances),
            ServiceLifetime.Transient => CreateInstance(descriptor),
            _ => throw new NotImplementedException()
        };
    }

    private object GetOrCreateSingleton(ServiceDescriptor descriptor)
    {
        if (_singletonInstances.TryGetValue(descriptor.ServiceType, out var instance))
            return instance;

        instance = Activator.CreateInstance(descriptor.ImplementationType)!;
        _singletonInstances[descriptor.ServiceType] = instance;
        return instance;
    }

    private object GetOrCreateScoped(ServiceDescriptor descriptor, Dictionary<Type, object> scopedInstances)
    {
        if (scopedInstances.TryGetValue(descriptor.ServiceType, out var instance))
            return instance;

        instance = Activator.CreateInstance(descriptor.ImplementationType)!;
        scopedInstances[descriptor.ServiceType] = instance;
        return instance;
    }

    private object CreateInstance(ServiceDescriptor descriptor)
    {
        return Activator.CreateInstance(descriptor.ImplementationType)!;
    }
}