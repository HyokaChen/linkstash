namespace Linkstash.Api.Collect;

public record CollectRequest(string Url, bool ExtractLinks = false);

public record ExtractRequest(string Url);
public record BatchRequest(
    IEnumerable<BatchItem> Items,
    string? GroupName = null,
    string? SourceUrl = null
);

public record BatchItem(string Url, string Title, string Translation, string? GroupId = null);

/// <summary>统一解析入口的输入。input 可含多个片段（URL / owner-repo slug / 关键词）。</summary>
public record ResolveRequest(string Input);

/// <summary>统一解析结果条目。isExisting=true 表示该 URL 已收藏，前端应置灰不可勾选。</summary>
public record ResolvedCandidate(
    string Input,
    string Url,
    string Title,
    string Translation,
    bool IsFallbackTitle,
    string Tags,
    bool IsExisting
);

/// <summary>书签文件导入请求。</summary>
public record ImportBookmarksRequest(string Html, string? GroupName = null);

/// <summary>书签解析出的候选条目。</summary>
public record ImportCandidate(
    string Url,
    string Title,
    string FolderPath,
    bool IsExisting
);

public record CandidateItem(string Url, string Title, string Translation, bool IsFallbackTitle);

public record CollectResult(
    string Id,
    string Url,
    string Title,
    string Translation,
    bool IsFallbackTitle,
    string CreatedAt,
    string Markdown,
    int ItemCount,
    string Tags
);

/// <summary>手动改标签请求。tags 传空字符串表示清空。</summary>
public record TagsRequest(string? Tags);
