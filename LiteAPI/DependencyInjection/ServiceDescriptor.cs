namespace LiteAPI;

public class ServiceDescriptor
{
    public Type ServiceType { get; }
    public Type ImplementationType { get; }
    public object? ImplementationInstance { get; set; }
    public ServiceLifetime Lifetime { get; }

    public ServiceDescriptor(Type serviceType, Type implementationType, ServiceLifetime lifetime)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
        Lifetime = lifetime;
    }

    public ServiceDescriptor(Type serviceType, object instance)
    {
        ServiceType = serviceType;
        ImplementationInstance = instance;
        ImplementationType = instance.GetType();
        Lifetime = ServiceLifetime.Singleton;
    }
}