/// <summary>
/// Opts the decorated handler out of automatic DataAnnotations validation.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SkipValidationAttribute : Attribute { }
