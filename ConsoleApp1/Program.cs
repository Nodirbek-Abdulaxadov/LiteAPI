using LiteAPI;

var builder = LiteWebApplication.CreateBuilder(args);
var app = builder.Build();

app.Get("/", ctx => Response.Ok("Welcome to LiteAPI!"));

app.Run();