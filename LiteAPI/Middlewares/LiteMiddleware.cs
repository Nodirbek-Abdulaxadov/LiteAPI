namespace LiteAPI.Middlewares;

public delegate Task LiteMiddleware(LiteHttpContext context, Func<Task> next);