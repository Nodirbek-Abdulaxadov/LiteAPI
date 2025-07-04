using lite;
using System.Net;

public class UsersRoutes : ILiteGroup
{
    public void Register(LiteWebApplicationGroup users)
    {
        var service = users.Inject<MockUserService>();

        users.Get("/", (HttpListenerRequest req, [FromQuery] QueryParams query) =>
        {
            return Response.OkJson(service.GetAll());
        });

        users.Get("/{id}", (HttpListenerRequest req, [FromRoute] int id) =>
        {
            var user = service.GetById(id);
            if (user == null)
                return Response.NotFound($"User with ID {id} not found.");
            return Response.OkJson(user);
        });

        users.Post("/", (HttpListenerRequest req, [FromForm] UserDto newUser) =>
        {
            if (newUser == null || string.IsNullOrWhiteSpace(newUser.Name) || string.IsNullOrWhiteSpace(newUser.Email))
                return Response.BadRequest("Invalid user data.");

            var createdUser = service.Add(newUser);
            return Response.Created($"/api/users/{createdUser.Id}", createdUser);
        });

        users.Put("/{id}", (HttpListenerRequest req, int id, UserDto updatedUser) =>
        {
            var user = service.GetById(id);
            if (user == null)
                return Response.NotFound($"User with ID {id} not found.");
            user.Name = updatedUser.Name;
            user.Email = updatedUser.Email;
            user.Age = updatedUser.Age;
            return Response.OkJson(user);
        });
    }
}