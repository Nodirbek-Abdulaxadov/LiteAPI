using LiteAPI.Routing;

public static class RequestBinder
{
    public static T Bind<T>(string formData) where T : new()
    {
        var obj = new T();
        BindProperties(obj, ParseFormData(formData));
        return obj;
    }

    public static object Bind(string formData, Type type)
    {
        var obj = Activator.CreateInstance(type)!;
        BindProperties(obj, ParseFormData(formData));
        return obj;
    }

    private static Dictionary<string, string> ParseFormData(string formData)
    {
        var nvc = HttpUtility.ParseQueryString(formData);
        return nvc.AllKeys
            .Where(k => k != null)
            .ToDictionary(k => k!, k => nvc[k!]!, StringComparer.OrdinalIgnoreCase);
    }

    private static void BindProperties(object target, IReadOnlyDictionary<string, string> values)
    {
        foreach (var prop in target.GetType().GetProperties())
        {
            if (!prop.CanWrite) continue;
            if (!values.TryGetValue(prop.Name, out var raw) || string.IsNullOrEmpty(raw)) continue;
            if (TypeConversion.TryConvert(raw, prop.PropertyType, out var converted))
                prop.SetValue(target, converted);
        }
    }

    public static Dictionary<string, string> ParseMultipartFormData(Stream stream, string contentType)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var boundaryToken = "boundary=";
        var idx = contentType.IndexOf(boundaryToken, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return result;

        var boundary = "--" + contentType[(idx + boundaryToken.Length)..].Trim().Trim('"');

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string? line;
        string? currentField = null;
        var currentValue = new StringBuilder();

        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith(boundary, StringComparison.Ordinal))
            {
                if (currentField != null)
                {
                    result[currentField] = currentValue.ToString().TrimEnd('\r', '\n');
                    currentField = null;
                    currentValue.Clear();
                }
            }
            else if (line.StartsWith("Content-Disposition", StringComparison.OrdinalIgnoreCase))
            {
                var nameIndex = line.IndexOf("name=\"", StringComparison.OrdinalIgnoreCase);
                if (nameIndex >= 0)
                {
                    nameIndex += 6;
                    var endIndex = line.IndexOf('"', nameIndex);
                    if (endIndex > nameIndex)
                        currentField = line[nameIndex..endIndex];
                }
                // Skip the blank line that separates the part header from the body.
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
        BindProperties(obj, ParseMultipartFormData(stream, contentType));
        return obj;
    }

    public static object BindQuery(this NameValueCollection query, Type type)
    {
        var obj = Activator.CreateInstance(type)!;
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? key in query.AllKeys)
        {
            if (key is null) continue;
            var v = query.Get(key);
            if (v != null) dict[key] = v;
        }
        BindProperties(obj, dict);
        return obj;
    }
}
