using Linkstash.Api.Collect;
using Linkstash.Api.Store;

namespace Linkstash.Api.Import;

/// <summary>
/// 书签文件导入。解析后先建 group 并返回候选，交由前端确认后再走
/// /api/collect/batch 入库，与粘贴批量同一流程。
/// </summary>
public static class ImportEndpoints
{
    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/api/import/bookmarks",
                async (ImportBookmarksRequest req, ICollectionStore store) =>
                {
                    var entries = BookmarkParser.Parse(req.Html);
                    if (entries.Count == 0)
                    {
                        return Results.BadRequest(
                            new { error = "未解析到书签，请确认是浏览器导出的 .html 文件" }
                        );
                    }

                    // 标准化去重
                    var unique = new Dictionary<string, BookmarkEntry>(
                        StringComparer.OrdinalIgnoreCase
                    );
                    foreach (var entry in entries)
                    {
                        if (UrlNormalizer.Normalize(entry.Url) is { } norm)
                            unique.TryAdd(norm, entry);
                    }

                    // 剔除已收藏
                    var existing = await store.ExistingNormalizedUrlsAsync(unique.Keys.ToList());
                    var candidates = unique
                        .Where(kv => !existing.Contains(kv.Key))
                        .Select(kv => new ImportCandidate(
                            kv.Value.Url,
                            kv.Value.Title,
                            kv.Value.FolderPath,
                            false
                        ))
                        .ToList();

                    var name = string.IsNullOrWhiteSpace(req.GroupName)
                        ? $"书签导入 {DateTime.Now:yyyy-MM-dd}"
                        : req.GroupName!;

                    var group = await store.CreateGroupAsync(name, null);

                    return Results.Json(
                        new
                        {
                            groupId = group.Id,
                            group.Name,
                            total = entries.Count,
                            skipped = unique.Count - candidates.Count,
                            candidates,
                        }
                    );
                }
            )
            .RequireAuthorization();

        app.MapGet(
                "/api/groups/{groupId}/items",
                async (string groupId, ICollectionStore store) =>
                {
                    var items = await store.GetGroupItemsAsync(groupId);
                    return Results.Json(new { items });
                }
            )
            .RequireAuthorization();
    }
}
