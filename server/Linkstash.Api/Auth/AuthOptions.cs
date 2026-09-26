namespace Linkstash.Api.Auth;

public class AuthOptions
{
    public string Password { get; set; } = "";
    public bool CookieSecure { get; set; }
}

public class ProxyOptions
{
    public string Url { get; set; } = "";
}
