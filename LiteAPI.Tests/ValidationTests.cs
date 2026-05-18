using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Xunit;

public class ValidationTests
{
    public class CreateUserDto
    {
        [Required(ErrorMessage = "Name is required.")]
        public string? Name { get; set; }

        [Range(1, 120, ErrorMessage = "Age must be between 1 and 120.")]
        public int Age { get; set; }

        [EmailAddress(ErrorMessage = "Email must be a valid address.")]
        public string? Email { get; set; }
    }

    private static void Reset() => ValidationPipeline.GlobalOptions = new ValidationOptions();

    [Fact]
    public void Missing_required_field_returns_400()
    {
        Reset();
        var router = new Router();
        router.Post("/users", ([FromBody] CreateUserDto dto) => Response.OkJson(dto));

        var resp = router.HandleRawRequest("POST", "/users", "{\"age\":30,\"email\":\"a@b.com\"}");

        Assert.Equal(400, resp.StatusCode);
        Assert.Contains("Name is required.", resp.GetBodyAsString());
    }

    [Fact]
    public void Range_violation_returns_400()
    {
        Reset();
        var router = new Router();
        router.Post("/users", ([FromBody] CreateUserDto dto) => Response.OkJson(dto));

        var resp = router.HandleRawRequest("POST", "/users", "{\"name\":\"Najim\",\"age\":999,\"email\":\"a@b.com\"}");

        Assert.Equal(400, resp.StatusCode);
        Assert.Contains("Age must be between 1 and 120.", resp.GetBodyAsString());
    }

    [Fact]
    public void Multiple_errors_are_collected()
    {
        Reset();
        var router = new Router();
        router.Post("/users", ([FromBody] CreateUserDto dto) => Response.OkJson(dto));

        var resp = router.HandleRawRequest("POST", "/users", "{\"age\":-5,\"email\":\"not-an-email\"}");

        var body = resp.GetBodyAsString();
        Assert.Equal(400, resp.StatusCode);
        Assert.Contains("Name is required.", body);
        Assert.Contains("Age must be between 1 and 120.", body);
        Assert.Contains("Email must be a valid address.", body);
    }

    [Fact]
    public void Valid_model_passes_through_to_handler()
    {
        Reset();
        var router = new Router();
        router.Post("/users", ([FromBody] CreateUserDto dto) => Response.OkJson(dto));

        var resp = router.HandleRawRequest("POST", "/users",
            "{\"name\":\"Najim\",\"age\":30,\"email\":\"najim@example.com\"}");

        Assert.Equal(200, resp.StatusCode);
        Assert.Contains("Najim", resp.GetBodyAsString());
    }

    [Fact]
    public void SkipValidation_on_handler_bypasses_validation()
    {
        Reset();
        var router = new Router();
        router.Post("/users-raw", Handlers.SkipHandler);

        var resp = router.HandleRawRequest("POST", "/users-raw", "{\"age\":-5,\"email\":\"x\"}");

        Assert.Equal(200, resp.StatusCode);
    }

    [Fact]
    public void Disabled_validation_in_options_bypasses_validation()
    {
        ValidationPipeline.GlobalOptions = new ValidationOptions { Enabled = false };
        var router = new Router();
        router.Post("/users", ([FromBody] CreateUserDto dto) => Response.OkJson(dto ?? new CreateUserDto()));

        var resp = router.HandleRawRequest("POST", "/users", "{\"age\":-5,\"email\":\"x\"}");

        Assert.Equal(200, resp.StatusCode);
        Reset();
    }

    [Fact]
    public void Compact_shape_emits_errors_array()
    {
        ValidationPipeline.GlobalOptions = new ValidationOptions { ResponseShape = ValidationResponseShape.Compact };
        var router = new Router();
        router.Post("/users", ([FromBody] CreateUserDto dto) => Response.OkJson(dto));

        var resp = router.HandleRawRequest("POST", "/users", "{\"age\":-5}");

        using var doc = JsonDocument.Parse(resp.GetBodyAsString());
        var errors = doc.RootElement.GetProperty("errors");
        Assert.True(errors.GetArrayLength() >= 2);
        Assert.True(errors[0].TryGetProperty("member", out _));
        Assert.True(errors[0].TryGetProperty("error", out _));

        Reset();
    }

    private static class Handlers
    {
        [SkipValidation]
        public static Response SkipHandler([FromBody] CreateUserDto dto) => Response.OkJson(dto ?? new CreateUserDto());
    }
}
