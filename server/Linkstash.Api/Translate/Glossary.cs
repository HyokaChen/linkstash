namespace Linkstash.Api.Translate;

/// <summary>
/// 术语表：在送给翻译 API 之前把固定译法写入文本，避免通用机器翻译给出错译。
/// 动机来自实测（MyMemory en→zh-CN）：
///   "Agent skills that fact-check the internet" → "……的代理技能"（agent 误译为代理）
///   "Manage repo issues and commit history"      → "管理回购问题……"（repo 误译为回购）
/// 预替换为中文后译文正确："智能体 skills ..." → "……智能体的技能"。
/// </summary>
public static class Glossary
{
    /// <summary>
    /// 词条按"实测错译 → 固定译法"录入：每条都先用 MyMemory en→zh-CN 实测过
    /// 错误输出，未实测的词不收录，避免凭感觉污染译文。
    /// 已修正的典型错译：agent→代理/客服代表、repo→回购、token→代币、
    /// pull request→提取请求、driver→合作车主、store→商店。
    /// </summary>
    private static readonly (string Term, string Translation)[] Terms =
    [
        // —— 确认存在错译，需强制 ——
        ("agent skills", "智能体技能"),
        ("ai agents", "AI 智能体"),
        ("ai agent", "AI 智能体"),
        ("multi-agent", "多智能体"),
        ("multi agent", "多智能体"),
        ("agent-based", "基于智能体的"),
        ("agents", "智能体"),
        ("agent", "智能体"),
        ("repo issues", "仓库 Issue"),
        ("repos", "仓库"),
        ("repo", "仓库"),
        ("token", "令牌"),
        ("driver", "驱动"),
        ("store", "存储"),
        ("pull request", "Pull Request"),
        ("issue", "Issue"),
        ("commits", "提交"),
        ("commit", "提交"),
        // —— 实测译文正确，固定下来防止回退 ——
        ("prompts", "提示词"),
        ("prompt", "提示词"),
        ("embeddings", "嵌入"),
        ("embedding", "嵌入"),
        ("fine-tuning", "微调"),
        ("context window", "上下文窗口"),
        ("middleware", "中间件"),
        ("endpoint", "端点"),
        ("dependency", "依赖"),
        ("dependencies", "依赖"),
        ("repository", "仓库"),
        ("toolchain", "工具链"),
        ("framework", "框架"),
        ("library", "库"),
        ("plugin", "插件"),
        ("extension", "扩展"),
        ("workflow", "工作流"),
        ("pipeline", "流水线"),
        ("daemon", "守护进程"),
        ("kernel", "内核"),
        ("compiler", "编译器"),
        ("parser", "解析器"),
        ("renderer", "渲染器"),
        ("frontend", "前端"),
        ("backend", "后端"),
        ("runtime", "运行时"),
        ("regex", "正则表达式"),
        ("cache", "缓存"),
        ("queue", "队列"),
        ("thread", "线程"),
        ("socket", "套接字"),
        ("payload", "载荷"),
        ("shell", "Shell"),
        ("script", "脚本"),
        ("toolkit", "工具包"),
        ("platform", "平台"),
        ("build", "构建"),
        ("cloud", "云"),
        ("server", "服务器"),
        ("native", "原生"),
        ("state", "状态"),
        ("suite", "套件"),
    ];

    /// <summary>
    /// 用固定译法替换文本中的术语。词边界匹配避免 "agent" 误伤 "multi-agent"，
    /// 长词优先保证 "agent skills" 先于 "agent" 被替换。
    /// </summary>
    public static string Apply(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        foreach (var (term, translation) in Terms.OrderByDescending(t => t.Term.Length))
            text = ReplaceWord(text, term, translation);

        return text;
    }

    private static string ReplaceWord(string text, string term, string replacement)
    {
        var pattern = $@"(?<![\w-]){System.Text.RegularExpressions.Regex.Escape(term)}(?![\w-])";
        return System.Text.RegularExpressions.Regex.Replace(
            text,
            pattern,
            _ => replacement,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
    }
}
