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

    /// <summary>
    /// Legacy entry point that returns only the text fields. New code should
    /// use <see cref="LiteAPI.Http.MultipartReader.Parse(Stream, string)"/> to
    /// get full <see cref="LiteAPI.Http.MultipartPart"/>s (file uploads, headers, binary data).
    /// </summary>
    public static Dictionary<string, string> ParseMultipartFormData(Stream stream, string contentType)
    {
        var parts = LiteAPI.Http.MultipartReader.Parse(stream, contentType);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parts)
        {
            if (p.IsFile) continue;
            result[p.Name] = p.AsString();
        }
        return result;
    }

    /// <summary>
    /// Bind a <c>multipart/form-data</c> body to <paramref name="type"/>.
    /// Properties typed as <see cref="LiteAPI.Http.MultipartPart"/>, <c>byte[]</c>,
    /// or <c>List&lt;MultipartPart&gt;</c> receive the uploaded files; all
    /// other properties bind from text fields (with full type conversion).
    /// </summary>
    public static object BindMultipart(this Stream stream, string contentType, Type type)
    {
        var obj = Activator.CreateInstance(type)!;
        var parts = LiteAPI.Http.MultipartReader.Parse(stream, contentType);

        var textFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var filesByName = new Dictionary<string, List<LiteAPI.Http.MultipartPart>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parts)
        {
            if (p.IsFile)
            {
                if (!filesByName.TryGetValue(p.Name, out var list))
                {
                    list = new List<LiteAPI.Http.MultipartPart>();
                    filesByName[p.Name] = list;
                }
                list.Add(p);
            }
            else
            {
                textFields[p.Name] = p.AsString();
            }
        }

        BindProperties(obj, textFields);

        foreach (var prop in type.GetProperties())
        {
            if (!prop.CanWrite) continue;
            if (!filesByName.TryGetValue(prop.Name, out var files) || files.Count == 0) continue;

            if (prop.PropertyType == typeof(LiteAPI.Http.MultipartPart))
                prop.SetValue(obj, files[0]);
            else if (prop.PropertyType == typeof(byte[]))
                prop.SetValue(obj, files[0].Data);
            else if (prop.PropertyType == typeof(List<LiteAPI.Http.MultipartPart>))
                prop.SetValue(obj, files);
            else if (prop.PropertyType == typeof(IReadOnlyList<LiteAPI.Http.MultipartPart>))
                prop.SetValue(obj, files);
        }

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
