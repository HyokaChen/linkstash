using System.Text.RegularExpressions;

namespace Linkstash.Api.Store;

/// <summary>
/// 自动打标签。用规则匹配而非模型分类：翻译接口（MyMemory）只做翻译，
/// 不具备分类能力，而规则匹配零成本、可预期、离线可用。
/// 中文译文与英文标题同时参与匹配，最多保留 <see cref="MaxTags"/> 个标签。
/// 命中为空时保持无标签，交由用户手动指定。
/// </summary>
public static class Tagger
{
    public const int MaxTags = 3;

    /// <summary>可用的标签全集，前端据此渲染选择器。</summary>
    public static readonly string[] Categories =
    [
        "AI",
        "创业",
        "csharp",
        "python",
        "运维",
        "爬虫",
        "客户端",
        "数据库",
        "算法",
        "金融",
        "检索",
        "测试",
        "游戏",
        "云服务",
        "rust",
        "zig",
        "p2p",
        "学习",
        "前端",
        "设计",
        "图像识别",
    ];

    private static readonly Dictionary<string, string[]> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AI"] = ["智能体", "大模型", "llm", "gpt", "claude", "agent skill", "mcp", "rag", "ai "],
        ["创业"] = ["创业", "融资", "商业模式", "mvp", "startup", "founder", "venture capital", "saas"],
        ["csharp"] = ["c#", "csharp", ".net", "dotnet", "asp.net", "blazor", "nuget", "roslyn"],
        ["python"] = ["python", "django", "flask", "fastapi", "pytest", "numpy", "pandas", "asyncio"],
        ["运维"] = ["运维", "kubernetes", "docker", "devops", "terraform", "ansible", "prometheus", "grafana"],
        ["爬虫"] = ["爬虫", "scrapy", "crawler", "spider", "selenium", "playwright", "beautifulsoup"],
        ["客户端"] = ["客户端", "electron", "tauri", "flutter", "react native", "desktop app"],
        ["数据库"] = ["数据库", "postgres", "mysql", "sqlite", "redis", "mongodb", "orm", "索引"],
        ["算法"] = ["算法", "leetcode", "neural network", "transformer", "machine learning", "deep learning"],
        ["金融"] = ["金融", "fintech", "trading", "blockchain", "量化", "支付", "crypto"],
        ["检索"] = ["检索", "search engine", "vector db", "retrieval", "全文检索", "搜索"],
        ["测试"] = ["测试", "test coverage", "e2e", "unit test", "integration test", "fuzzing"],
        ["游戏"] = ["游戏", "gamedev", "unity", "godot", "unreal", "game engine"],
        ["云服务"] = ["云服务", "cloud", "aws", "azure", "gcp", "serverless"],
        ["rust"] = ["rust", "cargo", "tokio"],
        ["zig"] = ["zig"],
        ["p2p"] = ["p2p", "peer-to-peer", "webrtc", "libp2p", "ipfs", "去中心化"],
        ["学习"] = ["学习", "教程", "tutorial", "course", "roadmap", "cheatsheet", "入门指南"],
        ["前端"] = ["前端", "react", "vue", "svelte", "angular", "css", "next.js", "nuxt", "sveltekit"],
        ["设计"] = ["设计", "figma", "ui/ux", "交互设计", "typography", "原型设计"],
        ["图像识别"] = ["图像识别", "computer vision", "object detection", "ocr", "yolo", "图像处理"],
    };

    /// <summary>根据标题与译文推断标签，返回 "#熊掌记/分类" 形式（逗号分隔）。</summary>
    public static string AutoTag(string title, string translation)
    {
        var en = (title ?? "").ToLower();
        var zh = translation ?? "";
        var hits = new List<string>();

        foreach (var (category, keywords) in Keywords)
        {
            var matched = keywords.Any(kw =>
                ContainsChinese(kw) ? zh.Contains(kw, StringComparison.Ordinal) : MatchWord(en, kw)
            );
            if (matched)
                hits.Add(category);
        }

        if (hits.Count == 0)
            return "";

        return string.Join(",", hits.Take(MaxTags).Select(ToHashTag));
    }

    /// <summary>规范用户输入的标签：补 #熊掌记/ 前缀、去重、去非法字符。</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        const string prefix = "#熊掌记/";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var part in raw.Split([',', '，', '、', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            var tag = part.Trim();
            if (!tag.StartsWith('#'))
                tag = prefix + tag;
            else if (!tag.StartsWith(prefix, StringComparison.Ordinal))
                tag = prefix + tag.TrimStart('#');

            if (tag.Length <= prefix.Length)
                continue;
            if (seen.Add(tag))
                result.Add(tag);
            if (result.Count >= MaxTags)
                break;
        }

        return string.Join(",", result);
    }

    public static string ToHashTag(string category) => "#熊掌记/" + category;

    private static bool ContainsChinese(string s) => s.Any(c => c is >= '一' and <= '鿿');

    /// <summary>英文关键词按词边界匹配，避免 design 误伤 designer 等更长词。</summary>
    private static bool MatchWord(string haystack, string keyword) =>
        Regex.IsMatch(
            haystack,
            $@"(^|[^a-z0-9]){Regex.Escape(keyword.ToLower())}([^a-z0-9]|$)",
            RegexOptions.CultureInvariant
        );
}
