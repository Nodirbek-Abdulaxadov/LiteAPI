using System.ComponentModel.DataAnnotations;

/// <summary>
/// Thin wrapper over <see cref="Validator.TryValidateObject(object, ValidationContext, ICollection{ValidationResult}, bool)"/>
/// that flattens <see cref="ValidationResult"/> entries into <see cref="ValidationFailure"/>s.
/// </summary>
public static class ModelValidator
{
    private static readonly IReadOnlyList<ValidationFailure> _empty = Array.Empty<ValidationFailure>();

    public static bool TryValidate(object? model, out IReadOnlyList<ValidationFailure> failures)
    {
        if (model is null)
        {
            failures = _empty;
            return true;
        }

        var ctx = new ValidationContext(model, serviceProvider: null, items: null);
        var results = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(model, ctx, results, validateAllProperties: true);

        if (ok)
        {
            failures = _empty;
            return true;
        }

        var list = new List<ValidationFailure>(results.Count);
        foreach (var r in results)
        {
            var message = r.ErrorMessage ?? "Invalid value.";
            if (r.MemberNames is not null)
            {
                var any = false;
                foreach (var member in r.MemberNames)
                {
                    any = true;
                    list.Add(new ValidationFailure(member ?? string.Empty, message));
                }
                if (!any)
                    list.Add(new ValidationFailure(string.Empty, message));
            }
            else
            {
                list.Add(new ValidationFailure(string.Empty, message));
            }
        }

        failures = list;
        return false;
    }
}
