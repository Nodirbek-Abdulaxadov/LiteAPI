namespace LiteAPI.Routing;

/// <summary>
/// Centralized string → CLR type conversion used by route / query / form binding.
/// Returns false on parse failure so callers can respond with 400 Bad Request
/// instead of letting <see cref="Convert.ChangeType(object, Type)"/> throw an
/// <see cref="InvalidCastException"/> and bubble up as a 500.
/// </summary>
internal static class TypeConversion
{
    public static bool TryConvert(string? raw, Type targetType, out object? value)
    {
        value = null;
        if (raw is null) return Nullable.GetUnderlyingType(targetType) != null || !targetType.IsValueType;

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying == typeof(string)) { value = raw; return true; }
        if (underlying == typeof(Guid))
        {
            if (Guid.TryParse(raw, out var g)) { value = g; return true; }
            return false;
        }
        if (underlying == typeof(bool))
        {
            if (bool.TryParse(raw, out var b)) { value = b; return true; }
            return false;
        }
        if (underlying == typeof(int))
        {
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { value = n; return true; }
            return false;
        }
        if (underlying == typeof(long))
        {
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { value = n; return true; }
            return false;
        }
        if (underlying == typeof(double))
        {
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) { value = n; return true; }
            return false;
        }
        if (underlying == typeof(decimal))
        {
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) { value = n; return true; }
            return false;
        }
        if (underlying == typeof(float))
        {
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) { value = n; return true; }
            return false;
        }
        if (underlying == typeof(DateTime))
        {
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)) { value = d; return true; }
            return false;
        }
        if (underlying == typeof(DateTimeOffset))
        {
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) { value = d; return true; }
            return false;
        }
        if (underlying == typeof(DateOnly))
        {
            if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) { value = d; return true; }
            return false;
        }
        if (underlying == typeof(TimeOnly))
        {
            if (TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) { value = t; return true; }
            return false;
        }
        if (underlying.IsEnum)
        {
            if (Enum.TryParse(underlying, raw, ignoreCase: true, out var e)) { value = e; return true; }
            return false;
        }

        // Fallback for less common numeric / primitive types via Convert.ChangeType.
        try
        {
            value = Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
