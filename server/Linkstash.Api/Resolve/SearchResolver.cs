using System.Text.RegularExpressions;
using System.Web;

namespace Linkstash.Api.Resolve;

public record SearchHit(string Url, string Title, string Snippet);

/// <summary>
/// 多引擎检索，slug 与关键词解析的兜底。
///
/// 实现要点来自本机 Mythology.Retrieval 的实测结论：
/// 1) DuckDuckGo 的 POST html.duckduckgo.com/html/ 在此网络下会失败（HTTP 层），
///    GET 才是可用路径——此前用 POST 导致检索恒为空。GET 返回相同 DOM 结构。
/// 2) Bing 结果链接为 /ck/a?...&amp;u=a1&lt;base64url&gt; 包装，需解码还原真实 URL。
///
/// 单引擎失败或无结果时自动降级到下一引擎；全部失败返回空列表，由调用方提示用户，
/// 不抛异常、不阻断其余片段。
/// </summary>
public partial class SearchResolver(HttpClient http, ILogger<SearchResolver> log)
{
    [GeneratedRegex(
        @"<a[^>]*class=""result__a""[^>]*href=""(?<href>[^""]+)""[^>]*>(?<title>[\s\S]*?)</a>",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex DdgResultRegex();

    [GeneratedRegex(@"uddg=(?<url>[^&]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UddgRegex();

    [GeneratedRegex(
        @"<td[^>]*class=""result__snippet""[^>]*>(?<snippet>[\s\S]*?)</td>",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex DdgSnippetRegex();

    [GeneratedRegex(
        @"<h2[^>]*>\s*<a[^>]*href=""(?<href>[^""]+)""[^>]*>(?<title>[\s\S]*?)</a>\s*</h2>",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex BingResultRegex();

    [GeneratedRegex(@"b_lineclamp[^>]*>(?<snippet>[\s\S]*?)</p>", RegexOptions.IgnoreCase)]
    private static partial Regex BingSnippetRegex();

    private const int MaxAttempts = 3;

    public async Task<List<SearchHit>> SearchAsync(
        string query,
        int limit = 5,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        // 引擎按可靠性排序：DDG(GET) 优先，Bing 作为补充。
        foreach (var engine in new[] { "duckduckgo", "bing" })
        {
            var raw = engine switch
            {
                "duckduckgo" => await SearchDuckDuckGoAsync(query, limit, ct),
                "bing" => await SearchBingAsync(query, limit, ct),
                _ => [],
            };

            // 必须过滤不相关结果：DDG 被限流时 Bing 会返回风马牛不相及的页面
            // （实测 "voidmuse github" 返回英国议员页面），直接入库会污染收藏库。
            var relevant = FilterRelevant(raw, query);
            if (relevant.Count > 0)
                return relevant;

            log.LogDebug(
                "engine {Engine} produced {Raw} hits but 0 relevant for: {Query}",
                engine,
                raw.Count,
                query
            );
        }

        log.LogWarning("all engines returned no relevant results for: {Query}", query);
        return [];
    }

    /// <summary>
    /// 相关性过滤。规则刻意保守——宁可返回空让用户手动补全，也不把不相关页面
    /// 写进收藏库。实测 Bing 在某些出口会返回完全无关的结果
    /// （"voidmuse github" 返回 LinkedIn 人名页），宽松阈值会直接污染收藏。
    ///
    /// 判定依据：
    /// 1) 只看标题与 URL，Snippet 不参与（摘要里泛词太多，会让无关页面蒙混过关）；
    /// 2) 排除搜索引擎自身域名（bing.com/duckduckgo.com 等跳转与广告链接）；
    /// 3) 站点限定词（github/huggingface…）不计入关键词——它们几乎出现在每个结果里；
    /// 4) 必须命中全部实词，且按整词边界匹配——"zzz" 曾误命中游戏名 "Zenless Zone ZZZ"。
    /// </summary>
    private static List<SearchHit> FilterRelevant(List<SearchHit> hits, string query)
    {
        var notSearchHost = hits.Where(h =>
                !SearchHostHosts.Any(s => h.Url.Contains(s, StringComparison.OrdinalIgnoreCase))
            )
            .ToList();

        var keywords = query
            .Split(
                [' ', '\t', '\n', ',', '，', '.', '/', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries
            )
            .Select(k => k.Trim().ToLowerInvariant())
            .Where(k => k.Length >= 2)
            .Distinct()
            .Where(k => !SiteQualifiers.Contains(k))
            .ToList();

        // 去掉站点限定词后没有实词（如只输入 "github"），不做相关性判定
        if (keywords.Count == 0)
            return notSearchHost;

        return notSearchHost
            .Where(h =>
            {
                var title = h.Title;
                var path = Uri.TryCreate(h.Url, UriKind.Absolute, out var u)
                    ? u.PathAndQuery
                    : h.Url;

                // 判定以 URL 为主：关键词必须出现在 URL 路径中。
                // 仅标题命中不算——游戏名 "Zenless Zone ZZZ" 让查询 "zzz"
                // 召回整站游戏 wiki，而 URL 里并不含 zzz（实测 inTitle=True/inPath=False）。
                var matchedInPath = keywords.Count(k => ContainsWord(path, k));
                if (matchedInPath == keywords.Count)
                    return true;

                // 全部关键词都出现在标题，且至少一个也出现在 URL，才认可
                var matchedInTitle = keywords.Count(k => ContainsWord(title, k));
                return matchedInTitle == keywords.Count && matchedInPath > 0;
            })
            .OrderBy(h => h.Url.Length)
            .ToList();
    }

    /// <summary>查询里的站点限定词：本身不作为关键词参与相关性判定。</summary>
    private static readonly HashSet<string> SiteQualifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "github",
        "huggingface",
        "gitlab",
        "gitee",
        "site",
        "com",
        "org",
        "io",
        "cn",
        "dev",
        "app",
        "www",
        "http",
        "https",
    };

    /// <summary>搜索引擎自身域名：出现在结果里说明是跳转/广告链接，不是目标页面。</summary>
    private static readonly string[] SearchHostHosts =
    [
        "bing.com",
        "duckduckgo.com",
        "google.com",
        "msn.com",
        "go.microsoft.com",
    ];

    /// <summary>
    /// 整词包含判定。词首前不得是字母数字，词尾后同理——
    /// 避免 "zzz" 命中 "ZenlessZZZ"、"ai" 命中 "said"。
    /// </summary>
    private static bool ContainsWord(string haystack, string word)
    {
        var index = 0;
        while (index < haystack.Length)
        {
            var at = haystack.IndexOf(word, index, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return false;

            var beforeOk = at == 0 || !char.IsLetterOrDigit(haystack[at - 1]);
            var end = at + word.Length;
            var afterOk = end >= haystack.Length || !char.IsLetterOrDigit(haystack[end]);
            if (beforeOk && afterOk)
                return true;

            index = at + 1;
        }
        return false;
    }

    /// <summary>DuckDuckGo HTML 端点。必须用 GET：POST 在此网络下不可用。</summary>
    private async Task<List<SearchHit>> SearchDuckDuckGoAsync(
        string query,
        int limit,
        CancellationToken ct
    )
    {
        var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
        var html = await FetchWithRetryAsync(url, null, DdgResultRegex(), ct);
        if (html.Length == 0)
            return [];

        var hits = new List<SearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in DdgResultRegex().Matches(html))
        {
            if (hits.Count >= limit)
                break;

            var raw = System.Net.WebUtility.HtmlDecode(m.Groups["href"].Value);
            var unwrapped = UddgRegex().Match(raw);
            var candidate = Uri.UnescapeDataString(
                unwrapped.Success ? unwrapped.Groups["url"].Value : raw
            );

            if (
                !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !seen.Add(uri.ToString())
            )
            {
                continue;
            }

            var snippetMatch = DdgSnippetRegex().Match(html, m.Index);
            var snippet =
                snippetMatch.Success && snippetMatch.Index - m.Index < 2000
                    ? StripTags(snippetMatch.Groups["snippet"].Value)
                    : "";

            hits.Add(new SearchHit(uri.ToString(), StripTags(m.Groups["title"].Value), snippet));
        }

        return hits;
    }

    /// <summary>
    /// Bing。结果链接是 /ck/a?...&amp;u=a1&lt;base64url&gt; 形式的跳转包装，
    /// 必须解码才能得到真实 URL，否则入库的是 bing.com 地址。
    /// </summary>
    private async Task<List<SearchHit>> SearchBingAsync(
        string query,
        int limit,
        CancellationToken ct
    )
    {
        var url =
            $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}&count={Math.Clamp(limit, 1, 50)}";
        var html = await FetchWithRetryAsync(url, null, BingResultRegex(), ct);
        if (html.Length == 0)
            return [];

        var hits = new List<SearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in BingResultRegex().Matches(html))
        {
            if (hits.Count >= limit)
                break;

            var decoded = DecodeBingRedirect(m.Groups["href"].Value);
            if (
                !Uri.TryCreate(decoded, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !seen.Add(uri.ToString())
            )
            {
                continue;
            }

            var snippetMatch = BingSnippetRegex().Match(html, m.Index);
            var snippet =
                snippetMatch.Success && snippetMatch.Index - m.Index < 2000
                    ? StripTags(snippetMatch.Groups["snippet"].Value)
                    : "";

            hits.Add(new SearchHit(uri.ToString(), StripTags(m.Groups["title"].Value), snippet));
        }

        return hits;
    }

    /// <summary>
    /// 解码 Bing 跳转链接：/ck/a?...&amp;u=a1&lt;base64url&gt;。
    /// 注意 "a1" 是 base64 数据本身的前缀而非独立标记——必须先取出完整的 u 参数值
    /// 再剥掉前两字符；直接在原始串里定位 "u=a1" 会把数据首字节一并切掉，导致乱码。
    /// </summary>
    private static string DecodeBingRedirect(string href)
    {
        var normalized = href.Replace("&amp;", "&", StringComparison.Ordinal);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            return normalized;

        var raw = HttpUtility.ParseQueryString(uri.Query)["u"];
        if (string.IsNullOrEmpty(raw))
            return normalized;

        var payload = raw.StartsWith("a1", StringComparison.Ordinal) ? raw[2..] : raw;
        if (payload.Length == 0)
            return normalized;

        try
        {
            var padded = payload.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            return decoded.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? decoded
                : normalized;
        }
        catch (FormatException)
        {
            return normalized;
        }
    }

    /// <summary>返回包含可解析结果的 HTML；全部尝试失败时返回空串。</summary>
    private async Task<string> FetchWithRetryAsync(
        string url,
        string? body,
        Regex expect,
        CancellationToken ct
    )
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (body is not null)
                    request.Content = new StringContent(body);
                request.Headers.TryAddWithoutValidation(
                    "Accept-Language",
                    "en-US,en;q=0.9,zh-CN;q=0.8"
                );

                using var resp = await http.SendAsync(request, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var html = await resp.Content.ReadAsStringAsync(ct);
                    if (expect.IsMatch(html))
                        return html;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                log.LogDebug(ex, "search attempt {Attempt} failed: {Url}", attempt, url);
            }

            if (attempt < MaxAttempts)
                await Task.Delay(400 * attempt, ct);
        }

        return "";
    }

    private static string StripTags(string html) =>
        System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", "")).Trim();
}
