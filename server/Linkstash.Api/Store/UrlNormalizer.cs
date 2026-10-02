namespace Linkstash.Api.Store;

/// <summary>
/// URL 规范化。用于去重与比对，避免同一地址因尾部斜杠、host 大小写、
/// fragment 而被当成多条不同收藏。
/// 实测规则：https://github.com/a/b/ 与 https://github.com/a/b 视为同一条；
/// 但 https://github.com/A/B 与 /a/b 是不同仓库，path 大小写敏感，不合并。
/// </summary>
public static class UrlNormalizer
{
    /// <summary>规范化用于去重与比对：小写 scheme/host、去尾部斜杠、去 fragment、保留 query。</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (
            !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        )
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Fragment = "",
        };

        var path = builder.Path.TrimEnd('/');
        builder.Path = path.Length == 0 ? "/" : path;

        return builder.Uri.AbsoluteUri;
    }

    /// <summary>展示用原样 URL，不做任何改写，仅 trim 空白。</summary>
    public static string Clean(string raw) => raw.Trim();
}
