public enum ValidationResponseShape
{
    /// <summary>RFC 7807 <c>application/problem+json</c> payload with grouped errors.</summary>
    ProblemDetails,
    /// <summary>Flat <c>{ errors: [{ member, error }, ...] }</c> payload.</summary>
    Compact
}

public sealed record ValidationFailure(string Member, string Error);

public sealed class ValidationOptions
{
    /// <summary>Globally enables or disables DataAnnotations validation on bound complex types.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Selects the wire shape used when the framework builds the 400 response.</summary>
    public ValidationResponseShape ResponseShape { get; set; } = ValidationResponseShape.ProblemDetails;

    /// <summary>
    /// Optional override. When set, the framework calls this instead of constructing
    /// the built-in body, letting the host return a fully custom <see cref="Response"/>.
    /// </summary>
    public Func<IReadOnlyList<ValidationFailure>, Response>? CustomResponse { get; set; }
}
