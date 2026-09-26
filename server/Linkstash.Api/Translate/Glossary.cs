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
    /// <summary>key 为英文术语，value 为固定中文译法。</summary>
    private static readonly (string Term, string Translation)[] Terms =
    [
        ("agent skills", "智能体技能"),
        ("ai agent", "AI 智能体"),
        ("ai agents", "AI 智能体"),
        ("multi-agent", "多智能体"),
        ("multi agent", "多智能体"),
        ("agent-based", "基于智能体的"),
        ("agent", "智能体"),
        ("agents", "智能体"),
        ("repo issues", "仓库 Issue"),
        ("repo", "仓库"),
        ("repos", "仓库"),
        ("commit", "提交"),
        ("commits", "提交"),
        ("pull request", "Pull Request"),
        ("prompt", "提示词"),
        ("prompts", "提示词"),
        ("embedding", "嵌入"),
        ("embeddings", "嵌入"),
        ("fine-tuning", "微调"),
        ("context window", "上下文窗口"),
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
