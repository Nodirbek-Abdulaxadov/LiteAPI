using LiteAPI.Configurations;

namespace lite;

internal class Configurations : LiteConfiguration
{
    public override void Initialize()
    {
        Urls = ["http://localhost:5005/"];
        LaunchBrowser = true;

        Append("ApiVersion", "1.0");
        Append("AppName", "LiteAPI Example");
        Append("Description", "A simple example of using LiteAPI with configurations.");
    }
}