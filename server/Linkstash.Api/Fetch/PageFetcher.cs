using System.Text;
using AngleSharp.Html.Parser;

namespace Linkstash.Api.Fetch;

public record LinkRef(string Url, string? Title);

public class PageFetchException(string url, string message) : Exception(message)
{
    public string Url { get; } = url;
}

public class PageFetcher(HttpClient http)
{
    private const int MaxBodyChars = 2_000_000;

    public async Task<(string Title, bool IsFallback, IReadOnlyList<LinkRef> Links)> FetchAsync(
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

        bool isFallback = false;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = FallbackTitle(url);
            isFallback = true;
        }

        var links = extractLinks ? ExtractLinks(doc, url) : [];

        return (title, isFallback, links);
    }

    private static List<LinkRef> ExtractLinks(AngleSharp.Dom.IDocument doc, string pageUrl)
    {
        var pageHost = new Uri(pageUrl).Host;
        var seen = new HashSet<string>();
        var result = new List<LinkRef>();
        foreach (var a in doc.QuerySelectorAll("a[href]"))
        {
            var href = a.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
                continue;
            if (!Uri.TryCreate(href, UriKind.Absolute, out var u))
                continue;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
                continue;
            if (u.Host == pageHost)
                continue;
            var normalized = u.ToString();
            if (!seen.Add(normalized))
                continue;
            var text = a.TextContent.Trim();
            result.Add(new LinkRef(normalized, string.IsNullOrEmpty(text) ? null : text));
        }
        return result;
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
