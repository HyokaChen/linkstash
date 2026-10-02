namespace Linkstash.Api.Store;

public record CollectionItem(
    string Id,
    string Url,
    string Title,
    string Translation,
    string? SourceUrl,
    bool IsFallbackTitle,
    string CreatedAt,
    string Tags,
    string? GroupId
);

public record ImportGroup(string Id, string Name, string? SourceUrl, string CreatedAt);

public record GroupSummary(string Id, string Name, int Count);

public interface ICollectionStore
{
    Task<CollectionItem> AddAsync(
        string url,
        string title,
        string translation,
        string? sourceUrl,
        bool isFallbackTitle,
        string tags = "",
        string? groupId = null
    );

    Task<int> AddBatchAsync(IEnumerable<CollectionItem> items);

    Task<(List<CollectionItem> Items, int Total)> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? tag = null
    );

    Task<bool> DeleteAsync(string id);

    Task<CollectionItem?> UpdateTagsAsync(string id, string tags);

    /// <summary>返回已收藏的规范化 URL 集合，用于批量导入前剔除重复项。</summary>
    Task<HashSet<string>> ExistingNormalizedUrlsAsync(IReadOnlyCollection<string> normalizedUrls);

    Task<ImportGroup> CreateGroupAsync(string name, string? sourceUrl);

    Task<List<CollectionItem>> GetGroupItemsAsync(string groupId);

    Task<List<GroupSummary>> ListGroupsAsync();
}
