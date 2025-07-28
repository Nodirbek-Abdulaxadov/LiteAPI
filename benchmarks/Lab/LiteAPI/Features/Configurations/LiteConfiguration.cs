public class LiteConfiguration
{
    public virtual string[] Urls { get; set; } = [];
    public virtual bool LaunchBrowser { get; set; }
    public Dictionary<string, object> Values { get; set; } = [];

    public virtual void Initialize()
    {
        if (Urls.Length == 0)
        {
            Urls = ["http://localhost:6070"];
        }
    }

    public void Append(string key, object value)
    {
        if (!Values.TryAdd(key, value))
        {
            Values[key] = value;
        }
    }

    public void LaunchBrowserIfEnabled()
    {
        if (LaunchBrowser)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Urls[0],
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch
            {
                Console.WriteLine($"Unable to launch browser automatically. Please open {Urls[0]} manually.");
            }
        }
    }
}