using LiteAPI;
using LiteAPI.Routing.Grouping;
using System.Net;

public class UsersRoutes : ILiteGroup
{
    private static List<UserDto> userList =
    [
        new UserDto { Id = 1, Name = "Alice1", Email = "test1@gmail.com", Age = 31 },
        new UserDto { Id = 2, Name = "Alice2", Email = "test2@gmail.com", Age = 32 },
        new UserDto { Id = 3, Name = "Alice3", Email = "test3@gmail.com", Age = 33 },
    ];

    public void Register(LiteWebApplicationGroup users)
    {
        users.Get("/", (HttpListenerRequest req, [FromQuery] QueryParams query) =>
        {
            return Response.OkJson(users);
        });

        users.Get("/{id}", (HttpListenerRequest req, [FromRoute] int id) =>
        {
            var user = userList.FirstOrDefault(u => u.Id == id);
            if (user == null)
                return Response.NotFound($"User with ID {id} not found.");
            return Response.OkJson(user);
        });

        users.Post("/", (HttpListenerRequest req, [FromForm] UserDto newUser) =>
        {
            if (newUser == null || string.IsNullOrWhiteSpace(newUser.Name) || string.IsNullOrWhiteSpace(newUser.Email))
                return Response.BadRequest("Invalid user data.");
            newUser.Id = userList.Max(u => u.Id) + 1;
            userList.Add(newUser);
            return Response.Created($"/{newUser.Id}", newUser);
        });

        users.Put("/{id}", (HttpListenerRequest req, int id, UserDto updatedUser) =>
        {
            var user = userList.FirstOrDefault(u => u.Id == id);
            if (user == null)
                return Response.NotFound($"User with ID {id} not found.");
            user.Name = updatedUser.Name;
            user.Email = updatedUser.Email;
            user.Age = updatedUser.Age;
            return Response.OkJson(user);
        });
    }
}