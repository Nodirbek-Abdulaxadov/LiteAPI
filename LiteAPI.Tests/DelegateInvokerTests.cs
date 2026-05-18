using LiteAPI.Routing;
using Xunit;

public class DelegateInvokerTests
{
    [Fact]
    public void Compile_invokes_static_returning_method()
    {
        Func<int, int, int> add = static (a, b) => a + b;
        var invoker = DelegateInvoker.Compile(add);

        var result = invoker([3, 4]);
        Assert.Equal(7, result);
    }

    [Fact]
    public void Compile_invokes_void_method_and_returns_null()
    {
        var hit = false;
        Action a = () => hit = true;
        var invoker = DelegateInvoker.Compile(a);

        var result = invoker([]);

        Assert.True(hit);
        Assert.Null(result);
    }

    [Fact]
    public void Compile_supports_instance_methods()
    {
        var box = new IntBox { Value = 10 };
        Func<int, int> add = box.AddTo;
        var invoker = DelegateInvoker.Compile(add);

        Assert.Equal(15, invoker([5]));
    }

    private sealed class IntBox
    {
        public int Value;
        public int AddTo(int x) => Value + x;
    }
}
