public class Response
{
    public int StatusCode { get; set; } = 200;
    public string ContentType { get; set; } = "text/plain";
    public byte[] Body { get; set; } = [];

    public string GetBodyAsString()
    {
        if (Body == null || Body.Length == 0)
            return string.Empty;

        return Encoding.UTF8.GetString(Body);
    }

    private static byte[] Encode(string text) => Encoding.UTF8.GetBytes(text);
    private static byte[] EncodeJson(object obj)
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj, options));
    }

    public static Response Html(string html, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "text/html",
        Body = Encoding.UTF8.GetBytes(html)
    };

    public static Response Ok(string text) => new()
    {
        StatusCode = 200,
        ContentType = "text/plain",
        Body = Encode(text)
    };

    public static Response OkJson(object obj) => new()
    {
        StatusCode = 200,
        ContentType = "application/json",
        Body = EncodeJson(obj)
    };

    public static Response BadRequest(string message = "Bad Request") => new()
    {
        StatusCode = 400,
        ContentType = "text/plain",
        Body = Encode(message)
    };

    public static Response NotFound(string message = "Not Found") => new()
    {
        StatusCode = 404,
        ContentType = "text/plain",
        Body = Encode(message)
    };

    public static Response NoContent() => new()
    {
        StatusCode = 204,
        ContentType = "text/plain",
        Body = []
    };

    public static Response Created(string location, object? obj = null)
    {
        var response = new Response
        {
            StatusCode = 201,
            ContentType = "application/json",
            Body = obj is not null ? EncodeJson(obj) : []
        };
        return response;
    }

    public static Response Accepted(string location, object? obj = null)
    {
        var response = new Response
        {
            StatusCode = 202,
            ContentType = "application/json",
            Body = obj is not null ? EncodeJson(obj) : []
        };
        return response;
    }

    public static Response Conflict(string message = "Conflict") => new()
    {
        StatusCode = 409,
        ContentType = "text/plain",
        Body = Encode(message)
    };

    public static Response Unauthorized(string message = "Unauthorized") => new()
    {
        StatusCode = 401,
        ContentType = "text/plain",
        Body = Encode(message)
    };

    public static Response Forbid(string message = "Forbidden") => new()
    {
        StatusCode = 403,
        ContentType = "text/plain",
        Body = Encode(message)
    };

    public static Response TooManyRequests(string message = "Too Many Requests") => new()
    {
        StatusCode = 429,
        ContentType = "text/plain",
        Body = Encoding.UTF8.GetBytes(message),
    };

    public static Response InternalServerError(string message = "Internal Server Error") => new()
    {
        StatusCode = 500,
        ContentType = "text/plain",
        Body = Encode(message)
    };

    public static Response Text(string text, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "text/plain",
        Body = Encode(text)
    };

    public static Response Json(object obj, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "application/json",
        Body = EncodeJson(obj)
    };
}