namespace Linkstash.Api.Store;

public record CollectionItem(
    string Id,
    string Url,
    string Title,
    string Translation,
    string? SourceUrl,
    bool IsFallbackTitle,
    string CreatedAt
);

public interface ICollectionStore
{
    Task<CollectionItem> AddAsync(
        string url,
        string title,
        string translation,
        string? sourceUrl,
        bool isFallbackTitle
    );
    Task<int> AddBatchAsync(IEnumerable<CollectionItem> items);
    Task<(List<CollectionItem> Items, int Total)> ListAsync(int page, int pageSize, string? search);
    Task<bool> DeleteAsync(string id);
}
