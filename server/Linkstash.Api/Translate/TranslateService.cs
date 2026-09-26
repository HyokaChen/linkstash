using System.Text.Json;
using Refit;

namespace Linkstash.Api.Translate;

/// <summary>
/// 翻译服务，基于 MyMemory 公开 API（零密钥，匿名 5000 字符/天）。
/// 接口契约已在部署网络实测：
///   GET https://api.mymemory.translated.net/get?q=...&langpair=en|zh-CN
///   成功 → {"responseData":{"translatedText":"示例域","match":0.85},"responseStatus":200,"quotaFinished":false}
///   超长 → responseStatus 403, "QUERY LENGTH LIMIT EXCEEDED. MAX ALLOWED QUERY : 500 CHARS"
///   额度耗尽 → quotaFinished=true
///   langpair 不支持 auto/aut，必须显式源语言，否则 403。
/// 文档：https://mymemory.translated.net/doc/spec.php
/// </summary>
public class TranslateService(
    IMyMemoryApi api,
    TranslateOptions options,
    ILogger<TranslateService> log
)
{
    private const string TargetLang = "zh-CN";

    /// <summary>MyMemory 单条 q 的官方上限为 500 字符（实测超限返回 403）。</summary>
    private const int MaxQueryLength = 500;

    public async Task<string> TranslateAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var source = DetectLanguage(text);
        if (source == "zh-CN")
        {
            // 已是中文，无需翻译，避免白白消耗每日额度。
            return text.Trim();
        }

        // 超长标题截断，防止触发 403 QUERY LENGTH LIMIT EXCEEDED。
        var query = text.Length > MaxQueryLength ? text[..MaxQueryLength] : text;

        var myMemoryQuery = new MyMemoryQuery
        {
            Text = query,
            LangPair = $"{source}|{TargetLang}",
            ContactEmail = string.IsNullOrWhiteSpace(options.ContactEmail)
                ? null
                : options.ContactEmail,
        };

        // 代理链路偶发 SSL EOF / 超时。重试 2 次（指数退避），避免瞬时抖动直接判失败。
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var raw = await api.Get(myMemoryQuery);
                var parsed = ParseResponse(raw);
                return parsed ?? Failure(text);
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex))
            {
                log.LogWarning(
                    ex,
                    "translate attempt {Attempt} failed, retrying: {Text}",
                    attempt,
                    query[..Math.Min(40, query.Length)]
                );
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt));
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "translate failed: {Text}", query[..Math.Min(40, query.Length)]);
                return Failure(text);
            }
        }
    }

    private const int MaxAttempts = 3;

    /// <summary>仅对瞬时网络故障重试；配额耗尽等确定性失败立即返回。</summary>
    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or IOException;

    /// <summary>翻译不可用时的可见标记，避免"未翻译"与"已翻译"无法区分。</summary>
    private static string Failure(string text) => $"(翻译失败) {text}";

    /// <summary>
    /// 解析 MyMemory 响应。已知结构优先取 responseData.translatedText；
    /// responseStatus != 200 或 quotaFinished 时按失败处理并保留可诊断信息。
    /// </summary>
    private string? ParseResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw.Trim());
            var root = doc.RootElement;

            var status = ReadInt(root, "responseStatus");
            if (status is not null && status != 200)
            {
                var details = ReadString(root, "responseDetails") ?? "unknown error";
                log.LogWarning("MyMemory returned {Status}: {Details}", status, details);
                return null;
            }

            if (
                root.TryGetProperty("quotaFinished", out var quota)
                && quota.ValueKind == JsonValueKind.True
            )
            {
                log.LogWarning("MyMemory daily quota exhausted (quotaFinished=true)");
                return null;
            }

            if (
                root.TryGetProperty("responseData", out var data)
                && data.TryGetProperty("translatedText", out var translated)
                && translated.ValueKind == JsonValueKind.String
            )
            {
                var value = translated.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }
        catch (JsonException)
        {
            var trimmed = raw.Trim();
            return trimmed.Length > 0 ? trimmed[..Math.Min(trimmed.Length, 500)] : null;
        }
    }

    private static int? ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.TryGetInt32(out var v) ? v : null;

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    /// <summary>
    /// 轻量语种识别（MyMemory 不支持 auto 源语言，故本地判定后再请求）。
    /// 覆盖收藏场景常见语言：中日文假名→ja、汉字→zh-CN、西里尔→ru、其余→en。
    /// </summary>
    private static string DetectLanguage(string text)
    {
        bool hasKana = false,
            hasHan = false,
            hasCyrillic = false,
            hasLatin = false;
        foreach (var c in text)
        {
            if (c is >= '぀' and <= 'ヿ' or '゠' and <= 'ヿ' or 'ㇰ' and <= 'ㇿ' or 'ｦ' and <= 'ﾟ')
                hasKana = true;
            else if (c is >= '一' and <= '鿿' or '㐀' and <= '䶿')
                hasHan = true;
            else if (c is >= 'Ѐ' and <= 'ӿ')
                hasCyrillic = true;
            else if (c is >= 'A' and <= 'z' or 'A' and <= 'Z')
                hasLatin = true;
        }

        if (hasKana)
            return "ja";
        if (hasHan)
            return "zh-CN";
        if (hasCyrillic)
            return "ru";
        _ = hasLatin;
        return "en";
    }
}
