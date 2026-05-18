using LiteAPI.Routing;
using Xunit;

public class TypeConversionTests
{
    [Theory]
    [InlineData("42", typeof(int), 42)]
    [InlineData("3.14", typeof(double), 3.14)]
    [InlineData("true", typeof(bool), true)]
    [InlineData("hello", typeof(string), "hello")]
    public void TryConvert_parses_primitives(string raw, Type t, object expected)
    {
        Assert.True(TypeConversion.TryConvert(raw, t, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void TryConvert_parses_Guid()
    {
        var g = Guid.NewGuid();
        Assert.True(TypeConversion.TryConvert(g.ToString(), typeof(Guid), out var value));
        Assert.Equal(g, value);
    }

    [Fact]
    public void TryConvert_parses_DateOnly_TimeOnly_DateTimeOffset()
    {
        Assert.True(TypeConversion.TryConvert("2026-05-18", typeof(DateOnly), out var dateOnly));
        Assert.Equal(new DateOnly(2026, 5, 18), dateOnly);

        Assert.True(TypeConversion.TryConvert("13:45:00", typeof(TimeOnly), out var timeOnly));
        Assert.Equal(new TimeOnly(13, 45, 0), timeOnly);

        Assert.True(TypeConversion.TryConvert("2026-05-18T10:00:00Z", typeof(DateTimeOffset), out var dto));
        Assert.IsType<DateTimeOffset>(dto);
    }

    [Theory]
    [InlineData("not-a-number", typeof(int))]
    [InlineData("not-a-guid", typeof(Guid))]
    [InlineData("xyz", typeof(bool))]
    [InlineData("foo", typeof(DateOnly))]
    public void TryConvert_returns_false_for_invalid_inputs(string raw, Type t)
    {
        Assert.False(TypeConversion.TryConvert(raw, t, out _));
    }

    [Fact]
    public void TryConvert_handles_nullable_value_types()
    {
        Assert.True(TypeConversion.TryConvert("5", typeof(int?), out var value));
        Assert.Equal(5, value);
    }

    private enum Color { Red, Green, Blue }

    [Fact]
    public void TryConvert_parses_enum_case_insensitive()
    {
        Assert.True(TypeConversion.TryConvert("green", typeof(Color), out var value));
        Assert.Equal(Color.Green, value);
    }
}
