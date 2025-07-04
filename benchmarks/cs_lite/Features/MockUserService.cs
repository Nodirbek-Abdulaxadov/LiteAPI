namespace lite;

public class MockUserService
{
    private readonly List<UserDto> _users = [];

    public MockUserService()
    {
        // Initial mock data
        _users.AddRange(
        [
            new UserDto { Id = 1, Name = "Alice", Email = "alice@example.com", Age = 28 },
            new UserDto { Id = 2, Name = "Bob", Email = "bob@example.com", Age = 34 },
            new UserDto { Id = 3, Name = "Charlie", Email = "charlie@example.com", Age = 25 }
        ]);
    }

    public IEnumerable<UserDto> GetAll() => _users;

    public UserDto? GetById(int id) => _users.FirstOrDefault(u => u.Id == id);

    public UserDto Add(UserDto user)
    {
        user.Id = _users.Count > 0 ? _users.Max(u => u.Id) + 1 : 1;
        _users.Add(user);
        return user;
    }

    public bool Update(int id, UserDto updated)
    {
        var existing = GetById(id);
        if (existing is null) return false;

        existing.Name = updated.Name;
        existing.Email = updated.Email;
        existing.Age = updated.Age;
        return true;
    }

    public bool Delete(int id)
    {
        var user = GetById(id);
        if (user is null) return false;
        _users.Remove(user);
        return true;
    }
}