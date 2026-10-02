using System.Text.RegularExpressions;

namespace Linkstash.Api.Resolve;

public record SearchHit(string Url, string Title, string Snippet);

/// <summary>
/// DuckDuckGo HTML 端点检索，作为 slug 与关键词解析的兜底。
/// 实测 POST https://html.duckduckgo.com/html/（body q=）返回 10 条 result__a，
/// 链接以 //duckduckgo.com/l/?uddg=&lt;encoded&gt; 包装，需解包后取真实 URL。
/// 偶发 202 空响应（限流特征），重试 3 次；仍失败则返回空列表并由调用方提示用户，
/// 不抛异常、不阻断其余片段。
/// </summary>
public partial class SearchResolver(HttpClient http, ILogger<SearchResolver> log)
{
    [GeneratedRegex(
        @"<a[^>]*class=""result__a""[^>]*href=""(?<href>[^""]+)""[^>]*>(?<title>[\s\S]*?)</a>",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex ResultLinkRegex();

    [GeneratedRegex(@"uddg=(?<url>[^&]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UddgRegex();

    [GeneratedRegex(
        @"<td[^>]*class=""result__snippet""[^>]*>(?<snippet>[\s\S]*?)</td>",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex SnippetRegex();

    private const string Endpoint = "https://html.duckduckgo.com/html/";
    private const int MaxAttempts = 3;

    public async Task<List<SearchHit>> SearchAsync(
        string query,
        int limit = 5,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        try
        {
            var html = await FetchWithRetryAsync(query, ct);
            if (html.Length == 0)
            {
                log.LogWarning("search returned no parsable body for: {Query}", query);
                return [];
            }

            var hits = new List<SearchHit>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in ResultLinkRegex().Matches(html))
            {
                if (hits.Count >= limit)
                    break;

                var raw = m.Groups["href"].Value;
                var unwrapped = UddgRegex().Match(raw);
                var url = Uri.UnescapeDataString(
                    unwrapped.Success ? unwrapped.Groups["url"].Value : raw
                );

                if (
                    !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                    || !seen.Add(uri.ToString())
                )
                {
                    continue;
                }

                var title = StripTags(m.Groups["title"].Value);
                var snippetMatch = SnippetRegex().Match(html, m.Index);
                var snippet =
                    snippetMatch.Success && snippetMatch.Index - m.Index < 2000
                        ? StripTags(snippetMatch.Groups["snippet"].Value)
                        : "";

                hits.Add(new SearchHit(uri.ToString(), title, snippet));
            }

            if (hits.Count == 0)
                log.LogWarning("search returned no parsable results for: {Query}", query);

            return hits;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "search failed for: {Query}", query);
            return [];
        }
    }

    /// <summary>返回含可解析结果的 HTML；全部尝试失败时返回空串。</summary>
    private async Task<string> FetchWithRetryAsync(string query, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var content = new FormUrlEncodedContent(
                    new Dictionary<string, string> { ["q"] = query }
                );
                using var resp = await http.PostAsync(Endpoint, content, ct);

                if (resp.IsSuccessStatusCode)
                {
                    var html = await resp.Content.ReadAsStringAsync(ct);
                    if (ResultLinkRegex().IsMatch(html))
                        return html;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                log.LogDebug(ex, "search attempt {Attempt} failed: {Query}", attempt, query);
            }

            if (attempt < MaxAttempts)
                await Task.Delay(400 * attempt, ct);
        }

        return "";
    }

    private static string StripTags(string html) =>
        Regex.Replace(html, "<[^>]+>", "").Replace("&amp;", "&").Trim();
}
