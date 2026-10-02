using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace Linkstash.Api.Fetch;

public record LinkRef(string Url, string? Title);

/// <summary>
/// 结构化条目：页面按「产品名称/产品介绍/URL」等标签罗列内容时，条目自带
/// 名称、链接与描述，无需再逐个抓取目标页面。
/// </summary>
public record ProductRef(string Name, string Url, string Description);

/// <summary>页面抓取结果，含标题、描述（用于翻译）与候选外链。</summary>
public record FetchResult(
    string Title,
    string? Description,
    bool IsFallback,
    IReadOnlyList<LinkRef> Links,
    IReadOnlyList<ProductRef> Products
);

public class PageFetchException(string url, string message) : Exception(message)
{
    public string Url { get; } = url;
}

public partial class PageFetcher(HttpClient http)
{
    private const int MaxBodyChars = 2_000_000;

    public async Task<FetchResult> FetchAsync(
        string url,
        bool extractLinks,
        CancellationToken ct = default
    )
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            throw new PageFetchException(url, $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

        string html;
        await using (var stream = await resp.Content.ReadAsStreamAsync())
        using (var reader = new StreamReader(stream))
        {
            var buffer = new char[81920];
            var sb = new StringBuilder();
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                sb.Append(buffer, 0, read);
                if (sb.Length > MaxBodyChars)
                    break;
            }
            html = sb.ToString();
        }

        var parser = new HtmlParser();
        var doc = await parser.ParseDocumentAsync(html);

        var title = FirstNonEmpty(
            doc.QuerySelector("title")?.TextContent,
            doc.QuerySelector("meta[property='og:title']")?.GetAttribute("content"),
            doc.QuerySelector("h1")?.TextContent
        )
            ?.Trim();

        title = CleanTitle(title, url);

        bool isFallback = false;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = FallbackTitle(url);
            isFallback = true;
        }

        var description = CleanDescription(
            FirstNonEmpty(
                ExtractGitHubAbout(doc),
                doc.QuerySelector("meta[property='og:description']")?.GetAttribute("content"),
                doc.QuerySelector("meta[name='description']")?.GetAttribute("content")
            )
                ?.Trim(),
            url
        );

        var links = extractLinks ? ExtractLinks(doc, url) : [];
        var products = extractLinks ? ExtractProducts(doc) : [];

        return new FetchResult(title, description, isFallback, links, products);
    }

    /// <summary>
    /// 取 GitHub 仓库页右侧 About 栏的简介 &lt;p&gt;。这是用户真正想读的描述：
    /// og:description 会在尾部追加 " - owner/repo"，About 栏则是干净原文
    /// （实测该页仅匹配到一个 description &lt;p&gt;，无歧义）。
    /// 选择器依据 About 标题与稳定类名前缀，避开 GitHub 每次发版都会变的
    /// CSS-module 哈希（形如 prc-PageLayout-PaneWrapper-pHPop）。
    /// </summary>
    private static string? ExtractGitHubAbout(AngleSharp.Dom.IDocument doc)
    {
        foreach (var heading in doc.QuerySelectorAll("h2"))
        {
            if (
                !string.Equals(
                    heading.TextContent.Trim(),
                    "About",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                continue;
            var text = heading
                .ParentElement?.QuerySelector("p[class*='description']")
                ?.TextContent.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        var byPrefix = doc.QuerySelector("p[class*='SidebarAbout-module__description']");
        var fallback = byPrefix?.TextContent.Trim();
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    /// <summary>
    /// GitHub 等站点会在 og:description 末尾追加 " - owner/repo"，
    /// 直接翻译会得到带仓库名的冗余译文，这里按当前 URL 去掉该后缀。
    /// </summary>
    private static string? CleanDescription(string? description, string pageUrl)
    {
        if (string.IsNullOrWhiteSpace(description))
            return description;

        var segments = new Uri(pageUrl).AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2 || string.IsNullOrWhiteSpace(segments[^1]))
            return description;

        var suffix = $" - {segments[^2]}/{segments[^1]}";
        var trimmed = description.EndsWith(suffix, StringComparison.Ordinal)
            ? description[..^suffix.Length]
            : description;
        return trimmed.Trim();
    }

    /// <summary>
    /// 收敛冗长标题。GitHub 的 &lt;title&gt; 形如
    /// "GitHub - owner/repo: 描述 · GitHub"，直接放进 markdown 首段可读性很差，
    /// 这里统一收敛为 "owner/repo"。
    /// </summary>
    private static string? CleanTitle(string? title, string pageUrl)
    {
        if (string.IsNullOrWhiteSpace(title))
            return title;

        var host = new Uri(pageUrl).Host;
        var value = title.Trim();

        if (host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
        {
            if (value.StartsWith("GitHub - ", StringComparison.OrdinalIgnoreCase))
                value = value["GitHub - ".Length..].Trim();

            value = Regex
                .Replace(value, @"\s*[|·]\s*GitHub\s*$", "", RegexOptions.IgnoreCase)
                .Trim();

            var segments = new Uri(pageUrl).AbsolutePath.Trim('/').Split('/');
            if (segments.Length >= 2)
            {
                var repo = $"{segments[^2]}/{segments[^1]}";
                if (value.StartsWith(repo, StringComparison.OrdinalIgnoreCase))
                {
                    // 短描述型标题（"owner/repo: 简介"）与长描述型（"… · GitHub"）
                    // 都要收敛为 owner/repo，否则整段描述会进入 markdown 首段。
                    return repo;
                }
            }
        }

        return value.TrimEnd('·', '|', '-').Trim();
    }

    /// <summary>非内容主机：图片 CDN、统计、微信自身资源，提取时忽略。</summary>
    private static readonly string[] IgnoredHostPatterns =
    [
        "mmbiz.qpic.cn",
        "res.wx.qq.com",
        "mp.weixin.qq.com",
        "wx.qq.com",
        "weiyun.com",
        "captcha.gtimg.com",
        "google-analytics.com",
        "googletagmanager.com",
        "doubleclick.net",
        "gstatic.com",
        "githubassets.com",
        "githubusercontent.com",
    ];

    private static bool IsContentUrl(string host, string pageHost) =>
        host != pageHost
        && !IgnoredHostPatterns.Any(p => host.EndsWith(p, StringComparison.OrdinalIgnoreCase))
        && !host.StartsWith("cdn.", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 提取外链。两种来源都要覆盖：
    /// 1) a[href] 属性——常规列表页；
    /// 2) 元素可见文本中的裸 URL——微信公众号等把链接存为纯文本（如
    ///    &lt;span leaf=""&gt;https://github.com/x/y&lt;/span&gt;），href 为空。
    /// 仅在正文容器（#js_content 等）内扫描，避免把 JS 打包产物里的 URL 混入。
    /// </summary>
    private static List<LinkRef> ExtractLinks(AngleSharp.Dom.IDocument doc, string pageUrl)
    {
        var pageHost = new Uri(pageUrl).Host;
        var seen = new HashSet<string>();
        var result = new List<LinkRef>();

        bool TryAdd(string rawUrl, string? anchorText)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
                return false;
            rawUrl = rawUrl.Trim().TrimEnd('.', ',', '，', '。', ';', '；', ')');
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var u))
                return false;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
                return false;
            if (!IsContentUrl(u.Host, pageHost))
                return false;
            if (!seen.Add(u.ToString()))
                return false;
            result.Add(
                new LinkRef(
                    u.ToString(),
                    string.IsNullOrWhiteSpace(anchorText) ? null : anchorText.Trim()
                )
            );
            return true;
        }

        // 1) 常规 a[href]
        foreach (var a in doc.QuerySelectorAll("a[href]"))
            TryAdd(a.GetAttribute("href") ?? "", a.TextContent);

        // 2) 正文容器内的裸文本 URL（微信公众号等把链接写在文本里）
        var contentRoot = doc.GetElementById("js_content") is { } wx
            ? wx
            : (doc.QuerySelector("article") ?? doc.QuerySelector("main") ?? doc.Body);
        if (contentRoot is not null)
        {
            foreach (var node in contentRoot.QuerySelectorAll("span, p, div, li, td, section"))
            {
                var text = node.TextContent;
                if (string.IsNullOrEmpty(text) || text.Length > 500)
                    continue;
                // 仅当整段文本本身就是 URL 时才取，避免长段落里挖碎片
                if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out _))
                    TryAdd(text, null);
            }
        }

        // 3) 正文纯文本中的 URL——腾讯微云等分享页把链接直接写进文本，
        //    既不是 a[href]，也不构成独立文本节点，前两条来源都会漏掉。
        //    实测该页正文含 14 个裸 URL，其中 4 个形如「昵称:https://…」。
        //    昵称经实测是分享者名而非标题，故统一不作为标题，标题走回源抓取。
        if (doc.Body is { } body)
        {
            var text = body.TextContent;
            if (text.Length > 0)
            {
                // 3a) 裸 URL：覆盖直接书写与「昵称:URL」两种形态
                foreach (Match m in BareUrlRegex().Matches(text))
                {
                    if (m.Groups["prefix"].Success)
                    {
                        var prefix = m.Groups["prefix"].Value.Trim();
                        if (prefix.Length < 2 || IsLinkPrefix(prefix))
                            continue;
                    }
                    TryAdd(m.Groups["url"].Value, null);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 匹配正文中的 URL，可带「短前缀:」（如分享者昵称）。
    /// 前缀限 24 字符且不含 URL 常用符号，避免把整段散文误判为前缀。
    /// </summary>
    [GeneratedRegex(
        @"(?:(?<prefix>[^:\s<>，。、）)]{1,24})[:：]\s*)?(?<url>https?://[^\s，。、）)""']+)",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex BareUrlRegex();

    /// <summary>前缀本身是链接片段（如 "http"、"www"）时，说明并非昵称，忽略该匹配。</summary>
    private static bool IsLinkPrefix(string prefix) =>
        prefix.Equals("http", StringComparison.OrdinalIgnoreCase)
        || prefix.Equals("https", StringComparison.OrdinalIgnoreCase)
        || prefix.Equals("www", StringComparison.OrdinalIgnoreCase);


    /// <summary>
    /// 提取结构化条目。适用于「Show HN 周报」「产品清单」这类正文里用固定标签
    /// 罗列多个条目的页面（微信公众号文章实测为 20 条，标签形如
    ///   URL: https://... / 产品名称: X / 产品作者: Y / 产品介绍: Z
    /// ）。这类页面的介绍往往已是中文，无需再抓取目标页或翻译。
    /// </summary>
    private static List<ProductRef> ExtractProducts(AngleSharp.Dom.IDocument doc)
    {
        var root =
            doc.GetElementById("js_content")
            ?? doc.QuerySelector("article")
            ?? doc.QuerySelector("main")
            ?? doc.Body;
        if (root is null)
            return [];

        // 先去 script 再取文本，保证标签按阅读顺序出现
        var text = root.TextContent.Replace('\u00a0', ' ');
        text = System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]*>", " "),
            @"\s+",
            " "
        );

        var blockRe = new System.Text.RegularExpressions.Regex(
            @"产品名称\s*[:：]\s*(?<name>.{1,80}?)\s*产品作者\s*[:：]\s*(?<author>.{0,60}?)\s*产品介绍\s*[:：]\s*(?<desc>.+?)(?=\s*\d+\s*[.、]\s*Show\s*HN|\s*产品名称\s*[:：]|\s*关键词|$)",
            System.Text.RegularExpressions.RegexOptions.Singleline
        );
        var urlRe = new System.Text.RegularExpressions.Regex(
            @"URL\s*[:：]\s*(?<url>https?://[^\s\[\]（）()]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        var products = new List<ProductRef>();
        var seen = new HashSet<string>();

        foreach (System.Text.RegularExpressions.Match m in blockRe.Matches(text))
        {
            var name = (m.Groups["name"].Value ?? "").Trim();
            var desc = (m.Groups["desc"].Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(desc))
                continue;

            // 向前回溯最近的 URL 标签作为该条目的链接
            var start = Math.Max(0, m.Index - 1500);
            var before = text[start..m.Index];
            var urls = urlRe.Matches(before);
            if (urls.Count == 0)
                continue;
            var url = urls[^1].Groups["url"].Value.TrimEnd('.', '。', ',', '，');
            if (!seen.Add(url))
                continue;

            products.Add(new ProductRef(name, url, desc));
        }

        return products;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string FallbackTitle(string url)
    {
        var uri = new Uri(url);
        var last = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault();
        if (!string.IsNullOrEmpty(last))
            return Uri.UnescapeDataString(last);
        return uri.Host;
    }
}
