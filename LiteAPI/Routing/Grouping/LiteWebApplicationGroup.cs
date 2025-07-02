namespace LiteAPI.Routing.Grouping;

public class LiteWebApplicationGroup
{
    private readonly Router _router;
    private readonly string _prefix;

    public LiteWebApplicationGroup(Router router, string prefix)
    {
        _router = router;
        _prefix = prefix.TrimEnd('/');

        if (_prefix == "")
            _prefix = "/";
    }

    private string Combine(string path)
    {
        if (path == "/")
            return _prefix;
        return $"{_prefix}/{path.TrimStart('/')}";
    }

    public void Get(string path, RequestHandler handler) => _router.Get(Combine(path), handler);
    public void Post(string path, RequestHandler handler) => _router.Post(Combine(path), handler);
    public void Put(string path, RequestHandler handler) => _router.Put(Combine(path), handler);
    public void Delete(string path, RequestHandler handler) => _router.Delete(Combine(path), handler);
    public void Patch(string path, RequestHandler handler) => _router.Patch(Combine(path), handler);
    public void Options(string path, RequestHandler handler) => _router.Options(Combine(path), handler);
    public void Head(string path, RequestHandler handler) => _router.Head(Combine(path), handler);
    public void Get(string path, Delegate handler) => _router.Get(Combine(path), handler);
    public void Post(string path, Delegate handler) => _router.Post(Combine(path), handler);
    public void Put(string path, Delegate handler) => _router.Put(Combine(path), handler);
    public void Delete(string path, Delegate handler) => _router.Delete(Combine(path), handler);
    public void Patch(string path, Delegate handler) => _router.Patch(Combine(path), handler);
    public void Options(string path, Delegate handler) => _router.Options(Combine(path), handler);
    public void Head(string path, Delegate handler) => _router.Head(Combine(path), handler);
}