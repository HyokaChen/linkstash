using System.Text.Json;

namespace Linkstash.Api.Resolve;

/// <summary>
/// 解析 owner/repo 半截标识。实测 GitHub 公开 API 无需鉴权即可按 slug 精确判定存在性，
/// 比搜索引擎更准且零成本；未命中返回 null，由调用方降级为检索。
/// </summary>
public class SlugResolver(HttpClient http, ILogger<SlugResolver> log)
{
    public async Task<string?> TryResolveRepoAsync(string slug, CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.GetAsync($"repos/{slug}", ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (
                doc.RootElement.TryGetProperty("html_url", out var el)
                && el.ValueKind == JsonValueKind.String
            )
            {
                return el.GetString();
            }
            return null;
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // 未命中/网络异常都不是致命情况，交由检索层兜底
            log.LogDebug(ex, "slug resolve failed: {Slug}", slug);
            return null;
        }
    }
}
