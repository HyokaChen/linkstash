using System.Text.RegularExpressions;

namespace Linkstash.Api.Import;

public record BookmarkEntry(string Url, string Title, string FolderPath);

/// <summary>
/// 解析 Netscape Bookmarks HTML（Chrome / Edge / Firefox 通用导出格式）。
/// 结构为嵌套的 <DL>，文件夹是 <H3>，书签是 <A HREF>。用栈记录 <DL> 深度，
/// 在 </DL> 处出栈丢弃该层，从而还原每个书签所属的文件夹路径。
/// </summary>
public static partial class BookmarkParser
{
    [GeneratedRegex(
        @"<DL[^>]*>|</DL\s*>|<DT>\s*<H3[^>]*>(?<folder>[\s\S]*?)</H3\s*>|<DT>\s*<A\s+[^>]*?href=""(?<url>[^""]+)""[^>]*>(?<title>[\s\S]*?)</A\s*>",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex TokenRegex();

    public static List<BookmarkEntry> Parse(string html)
    {
        var entries = new List<BookmarkEntry>();
        if (string.IsNullOrWhiteSpace(html))
            return entries;

        var stack = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in TokenRegex().Matches(html))
        {
            var token = m.Value;

            if (token.StartsWith("<DL", StringComparison.OrdinalIgnoreCase))
            {
                stack.Add(string.Empty);
            }
            else if (token.StartsWith("</DL", StringComparison.OrdinalIgnoreCase))
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
            }
            else if (m.Groups["folder"].Success)
            {
                var name = Decode(m.Groups["folder"].Value);
                if (stack.Count > 0)
                    stack[^1] = name;
            }
            else if (m.Groups["url"].Success)
            {
                var url = m.Groups["url"].Value.Trim();
                if (url.Length == 0 || !seen.Add(url))
                    continue;

                var title = Decode(m.Groups["title"].Value);
                // 过滤浏览器导出的占位条目（如 javascript: 与内部占位页）
                if (
                    url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("place:", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                var path = string.Join(
                    "/",
                    stack.Where(s => !string.IsNullOrWhiteSpace(s))
                );
                entries.Add(new BookmarkEntry(url, title, path));
            }
        }

        return entries;
    }

    private static string Decode(string s) =>
        System.Net.WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", "")).Trim();
}
