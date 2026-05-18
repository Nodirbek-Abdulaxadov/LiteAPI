namespace LiteAPI.Routing;

/// <summary>
/// Compiles a fast <see cref="Delegate"/> invocation path that avoids the
/// per-call reflection cost of <see cref="Delegate.DynamicInvoke"/>. The
/// invoker is cached per delegate and reused for every request.
/// </summary>
internal static class DelegateInvoker
{
    public static Func<object?[], object?> Compile(Delegate handler)
    {
        var method = handler.Method;
        var parameters = method.GetParameters();
        var argsParam = Expression.Parameter(typeof(object?[]), "args");

        var callArgs = new Expression[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            var arrayAccess = Expression.ArrayIndex(argsParam, Expression.Constant(i));
            callArgs[i] = Expression.Convert(arrayAccess, parameters[i].ParameterType);
        }

        Expression body = handler.Target is not null
            ? Expression.Call(Expression.Constant(handler.Target), method, callArgs)
            : Expression.Call(method, callArgs);

        if (method.ReturnType == typeof(void))
        {
            var block = Expression.Block(body, Expression.Constant(null, typeof(object)));
            return Expression.Lambda<Func<object?[], object?>>(block, argsParam).Compile();
        }

        var convert = Expression.Convert(body, typeof(object));
        return Expression.Lambda<Func<object?[], object?>>(convert, argsParam).Compile();
    }
}
