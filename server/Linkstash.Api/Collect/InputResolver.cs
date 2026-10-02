using System.Text.RegularExpressions;

namespace Linkstash.Api.Collect;

public enum InputKind
{
    Url,
    Slug,
    SearchQuery,
}

public record ResolvedInput(InputKind Kind, string Raw, string? Url, string? DisplayHint);

/// <summary>
/// 把用户粘贴的任意文本拆分为三类输入：完整 URL、owner/repo 半截标识、自由搜索短语。
///
/// 切分策略：先按强分隔符（中英文逗号/顿号/分号/换行）切成"段"，
/// 再在每段内用正则提取所有完整 URL —— 这样 "url1 url2"（空格分隔的两个地址）
/// 也能各自识别；而 "davia labs" 这种多词短语不会被空格拆散，整体作为检索短语。
///
/// 关键行为：无法识别的片段不再被静默丢弃，而是转为 SearchQuery 交给检索层，
/// 避免用户粘贴 "davialabs/davia" 后界面毫无反应。
/// </summary>
public static partial class InputResolver
{
    // 强分隔符：逗号、顿号、分号、换行、制表符（不含空格，空格不足以可靠分段）
    [GeneratedRegex(@"[，,、；;\r\n\t]+")]
    private static partial Regex SegmentSplitRegex();

    // 段内提取完整 URL（遇到分隔符如空格/中文标点即止）
    [GeneratedRegex(@"https?://[^\s，。、；;""')<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    // owner/repo 半截标识
    [GeneratedRegex(@"^[\w.\-]+/[\w.\-]+$")]
    private static partial Regex SlugRegex();

    public static List<ResolvedInput> Resolve(string text)
    {
        var result = new List<ResolvedInput>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        foreach (var rawSegment in SegmentSplitRegex().Split(text))
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0 || segment.Length > 2000)
                continue;

            var urls = UrlRegex().Matches(segment);
            foreach (Match m in urls)
            {
                var url = m.Value.TrimEnd('.', ',', '，', '。', ';', '；', ')');
                result.Add(new ResolvedInput(InputKind.Url, url, url, null));
            }

            // 去掉已识别的 URL 后，剩下的文字是检索短语或 slug
            var remainder = UrlRegex().Replace(segment, " ").Trim();
            if (remainder.Length == 0)
                continue;

            foreach (var token in remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length > 200)
                    continue;

                if (SlugRegex().IsMatch(token))
                    result.Add(
                        new ResolvedInput(InputKind.Slug, token, null, $"按仓库解析：{token}")
                    );
                else
                    result.Add(
                        new ResolvedInput(InputKind.SearchQuery, token, null, $"将搜索：{token}")
                    );
            }

            // 整段含空格且不是纯 URL 组合时，把整段作为一条检索短语保留，
            // 避免 "davia labs" 被拆成两个独立单词各自检索导致零结果。
            if (remainder.Contains(' '))
            {
                result.Add(
                    new ResolvedInput(
                        InputKind.SearchQuery,
                        remainder,
                        null,
                        $"将搜索：{remainder}"
                    )
                );
            }
        }

        return Dedupe(result);
    }

    private static List<ResolvedInput> Dedupe(List<ResolvedInput> inputs)
    {
        var seen = new HashSet<(InputKind, string)>();
        var output = new List<ResolvedInput>(inputs.Count);
        foreach (var input in inputs)
        {
            if (seen.Add((input.Kind, input.Raw)))
                output.Add(input);
        }
        return output;
    }
}
