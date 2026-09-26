namespace Linkstash.Api.Auth;

public class AuthOptions
{
    public string Password { get; set; } = "";
    public bool CookieSecure { get; set; }
}

public class ProxyOptions
{
    /// <summary>
    /// 抓取页面时使用的代理（可选，留空则直连）。
    /// 仅影响 PageFetcher，不影响翻译链路。
    /// </summary>
    public string FetchUrl { get; set; } = "";
}
