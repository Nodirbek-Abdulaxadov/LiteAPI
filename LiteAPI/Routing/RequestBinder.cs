public static class RequestBinder
{
    public static T Bind<T>(string formData) where T : new()
    {
        var obj = new T();
        var nvc = System.Web.HttpUtility.ParseQueryString(formData);
        var parsed = nvc.AllKeys
            .Where(k => k != null)
            .ToDictionary(k => k!, k => nvc[k!]!, StringComparer.OrdinalIgnoreCase);

        foreach (var prop in typeof(T).GetProperties())
        {
            if (parsed.TryGetValue(prop.Name, out var value))
            {
                var converted = Convert.ChangeType(value, prop.PropertyType, CultureInfo.InvariantCulture);
                prop.SetValue(obj, converted);
            }
        }

        return obj;
    }

    public static object Bind(string formData, Type type)
    {
        var obj = Activator.CreateInstance(type)!;
        var nvc = System.Web.HttpUtility.ParseQueryString(formData);
        var parsed = nvc.AllKeys
            .Where(k => k != null)
            .ToDictionary(k => k!, k => nvc[k!]!, StringComparer.OrdinalIgnoreCase);

        foreach (var prop in type.GetProperties())
        {
            if (parsed.TryGetValue(prop.Name, out var value) && !string.IsNullOrEmpty(value))
            {
                object? converted;

                if (prop.PropertyType.IsEnum)
                {
                    converted = Enum.Parse(prop.PropertyType, value, ignoreCase: true);
                }
                else
                {
                    // Nullable type check
                    var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                }

                prop.SetValue(obj, converted);
            }
        }

        return obj;
    }

    public static Dictionary<string, string> ParseMultipartFormData(Stream stream, string contentType)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var boundary = "--" + contentType.Split("boundary=")[1];
        using var reader = new StreamReader(stream);

        string? line;
        string? currentField = null;
        StringBuilder currentValue = new();

        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith(boundary))
            {
                if (currentField != null)
                {
                    result[currentField] = currentValue.ToString().TrimEnd('\r', '\n');
                    currentField = null;
                    currentValue.Clear();
                }
            }
            else if (line.StartsWith("Content-Disposition"))
            {
                var nameIndex = line.IndexOf("name=\"", StringComparison.OrdinalIgnoreCase);
                if (nameIndex >= 0)
                {
                    nameIndex += 6;
                    var endIndex = line.IndexOf("\"", nameIndex, StringComparison.OrdinalIgnoreCase);
                    currentField = line.Substring(nameIndex, endIndex - nameIndex);
                }
                // Skip next empty line
                reader.ReadLine();
            }
            else if (currentField != null)
            {
                currentValue.AppendLine(line);
            }
        }

        return result;
    }
    
    public static object BindMultipart(this Stream stream, string contentType, Type type)
    {
        var obj = Activator.CreateInstance(type)!;
        var formDict = ParseMultipartFormData(stream, contentType);

        foreach (var prop in type.GetProperties())
        {
            if (formDict.TryGetValue(prop.Name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                object? converted;

                if (prop.PropertyType.IsEnum)
                {
                    converted = Enum.Parse(prop.PropertyType, value, ignoreCase: true);
                }
                else
                {
                    var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                }

                prop.SetValue(obj, converted);
            }
        }

        return obj;
    }

    public static object BindQuery(this NameValueCollection query, Type type)
    {
        var obj = Activator.CreateInstance(type)!;
        var props = type.GetProperties();

        foreach (var prop in props)
        {
            var value = query.Get(prop.Name);
            if (!string.IsNullOrEmpty(value))
            {
                object? converted;

                if (prop.PropertyType.IsEnum)
                {
                    converted = Enum.Parse(prop.PropertyType, value, ignoreCase: true);
                }
                else
                {
                    var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                }

                prop.SetValue(obj, converted);
            }
        }

        return obj;
    }
}