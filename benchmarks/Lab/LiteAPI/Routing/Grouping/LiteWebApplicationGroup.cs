public class LiteWebApplicationGroup
{
    private readonly Router _router;
    private readonly string _prefix;
    private readonly ServiceCollection? _services;

    public LiteWebApplicationGroup(Router router, string prefix, ServiceCollection? services = null)
    {
        _router = router;
        _prefix = prefix.TrimEnd('/');
        _services = services;

        if (_prefix == "")
            _prefix = "/";
    }

    private string Combine(string path)
    {
        if (path == "/")
            return _prefix;
        return $"{_prefix}/{path.TrimStart('/')}";
    }

    public T Inject<T>() where T : class
    {
        if (_services == null)
            throw new InvalidOperationException("ServiceCollection is not available for injection in this group.");

        // DRY tarzda, har safar yangisini bermaydi, ro'yxatdan o'tgan qoidaga ko'ra beradi
        return (T)_services.Resolve(typeof(T), []);
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