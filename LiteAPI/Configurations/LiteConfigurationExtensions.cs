using LiteAPI.Configurations;

namespace LiteAPI;

public static class LiteConfigurationExtensions
{
    public static T? Get<T>(this LiteConfiguration config, string key, T? defaultValue = default)
    {
        if (config.Values.TryGetValue(key, out var value))
        {
            if (value is T tValue)
                return tValue;

            return (T)Convert.ChangeType(value, typeof(T));
        }
        return defaultValue;
    }

    public static dynamic? Get(this LiteConfiguration config, string key)
    {
        if (config.Values.TryGetValue(key, out var value))
        {
            return value;
        }

        throw new KeyNotFoundException($"Key '{key}' not found in configuration values.");
    }
}