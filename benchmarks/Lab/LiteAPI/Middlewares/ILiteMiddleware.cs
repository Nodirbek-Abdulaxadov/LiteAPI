public interface ILiteMiddleware
{
    Task InvokeAsync(LiteHttpContext context, Func<Task> next);
}