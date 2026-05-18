public static class ValidationPipeline
{
    /// <summary>
    /// Process-wide validation configuration. Set by <c>AddValidation</c>; left null when
    /// the host never opted in (in which case the router treats it as enabled-by-default).
    /// </summary>
    public static ValidationOptions? GlobalOptions { get; internal set; }

    /// <summary>True when validation should run. Defaults to <c>true</c> when no options were registered.</summary>
    internal static bool IsEnabled
        => GlobalOptions is null || GlobalOptions.Enabled;

    /// <summary>
    /// Builds the 400 response for the supplied <paramref name="failures"/>, honouring
    /// <see cref="ValidationOptions.CustomResponse"/> when one is configured.
    /// </summary>
    public static Response? BuildFailureResponse(IReadOnlyList<ValidationFailure> failures)
    {
        if (failures is null || failures.Count == 0)
            return null;

        var opts = GlobalOptions;

        if (opts?.CustomResponse is { } custom)
            return custom(failures);

        var shape = opts?.ResponseShape ?? ValidationResponseShape.ProblemDetails;
        return shape switch
        {
            ValidationResponseShape.Compact => BuildCompact(failures),
            _ => BuildProblemDetails(failures)
        };
    }

    private static Response BuildProblemDetails(IReadOnlyList<ValidationFailure> failures)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var f in failures)
        {
            var key = string.IsNullOrEmpty(f.Member) ? string.Empty : f.Member;
            if (!errors.TryGetValue(key, out var bucket))
            {
                bucket = new List<string>();
                errors[key] = bucket;
            }
            bucket.Add(f.Error);
        }

        var body = new
        {
            type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            title = "One or more validation errors occurred.",
            status = 400,
            errors = errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray())
        };

        var resp = Response.Json(body, statusCode: 400);
        resp.ContentType = "application/problem+json; charset=utf-8";
        return resp;
    }

    private static Response BuildCompact(IReadOnlyList<ValidationFailure> failures)
    {
        var body = new
        {
            errors = failures.Select(f => new { member = f.Member, error = f.Error }).ToArray()
        };
        return Response.Json(body, statusCode: 400);
    }
}
