public class ServiceProvider(ServiceCollection services)
{
    public T GetService<T>() where T : class
    {
        return (T)services.Resolve(typeof(T), []);
    }

    public object GetService(Type type)
    {
        return services.Resolve(type, []);
    }
}