public class Response
{
    public int StatusCode { get; set; } = 200;
    public string ContentType { get; set; } = "text/plain";
    public byte[] Body { get; set; } = [];

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string GetBodyAsString()
        => Body is { Length: > 0 } ? Encoding.UTF8.GetString(Body) : string.Empty;

    private static byte[] Encode(string text) => Encoding.UTF8.GetBytes(text);
    private static byte[] EncodeJson(object obj) => JsonSerializer.SerializeToUtf8Bytes(obj, _jsonOptions);

    public static Response Html(string html, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "text/html; charset=utf-8",
        Body = Encode(html)
    };

    public static Response Ok(string text) => new()
    {
        StatusCode = 200,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(text)
    };

    public static Response OkJson(object obj) => new()
    {
        StatusCode = 200,
        ContentType = "application/json; charset=utf-8",
        Body = EncodeJson(obj)
    };

    public static Response BadRequest(string message = "Bad Request") => new()
    {
        StatusCode = 400,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response NotFound(string message = "Not Found") => new()
    {
        StatusCode = 404,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response NoContent() => new()
    {
        StatusCode = 204,
        ContentType = "text/plain; charset=utf-8",
        Body = []
    };

    public static Response Created(string location, object? obj = null) => new()
    {
        StatusCode = 201,
        ContentType = "application/json; charset=utf-8",
        Body = obj is not null ? EncodeJson(obj) : []
    };

    public static Response Accepted(string location, object? obj = null) => new()
    {
        StatusCode = 202,
        ContentType = "application/json; charset=utf-8",
        Body = obj is not null ? EncodeJson(obj) : []
    };

    public static Response Conflict(string message = "Conflict") => new()
    {
        StatusCode = 409,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response Unauthorized(string message = "Unauthorized") => new()
    {
        StatusCode = 401,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response Forbid(string message = "Forbidden") => new()
    {
        StatusCode = 403,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response TooManyRequests(string message = "Too Many Requests") => new()
    {
        StatusCode = 429,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response PayloadTooLarge(string message = "Payload Too Large") => new()
    {
        StatusCode = 413,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response InternalServerError(string message = "Internal Server Error") => new()
    {
        StatusCode = 500,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(message)
    };

    public static Response Text(string text, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "text/plain; charset=utf-8",
        Body = Encode(text)
    };

    public static Response Json(object obj, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "application/json; charset=utf-8",
        Body = EncodeJson(obj)
    };

    public static Response Bytes(byte[] body, string contentType, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = contentType,
        Body = body
    };
}
