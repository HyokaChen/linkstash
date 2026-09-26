using Linkstash.Api.Fetch;
using Linkstash.Api.Store;
using Linkstash.Api.Translate;

namespace Linkstash.Api.Collect;

public static class CollectEndpoints
{
    public static void MapCollectEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/api/collect",
                async (
                    CollectRequest req,
                    PageFetcher fetcher,
                    TranslateService translator,
                    ICollectionStore store
                ) =>
                {
                    if (string.IsNullOrWhiteSpace(req.Url))
                        return Results.BadRequest(new { error = "url required" });

                    if (req.ExtractLinks)
                    {
                        var candidates = await ExtractCandidatesAsync(req.Url, fetcher, translator);
                        return Results.Json(new { candidates });
                    }

                    try
                    {
                        var (title, isFallback, _) = await fetcher.FetchAsync(req.Url, false);
                        var translation = await translator.TranslateAsync(title);
                        var item = await store.AddAsync(
                            req.Url,
                            title,
                            translation,
                            null,
                            isFallback
                        );
                        return Results.Json(
                            new CollectResult(
                                item.Id,
                                item.Url,
                                item.Title,
                                item.Translation,
                                item.IsFallbackTitle,
                                item.CreatedAt,
                                $"[{item.Title}]({item.Url}) => {item.Translation}"
                            )
                        );
                    }
                    catch (PageFetchException ex)
                    {
                        return Results.Json(
                            new { url = req.Url, error = $"fetch failed: {ex.Message}" }
                        );
                    }
                    catch (Exception ex)
                        when (ex is HttpRequestException or TaskCanceledException or IOException)
                    {
                        // 网络/代理瞬时失败（如代理 SSL EOF、超时）不应冒泡成 500，
                        // 否则用户看到"服务器错误"而非可操作的提示。
                        return Results.Json(
                            new { url = req.Url, error = $"network error: {ex.Message}" }
                        );
                    }
                }
            )
            .RequireAuthorization();

        app.MapPost(
                "/api/collect/extract",
                async (ExtractRequest req, PageFetcher fetcher, TranslateService translator) =>
                {
                    if (string.IsNullOrWhiteSpace(req.Url))
                        return Results.BadRequest(new { error = "url required" });
                    var candidates = await ExtractCandidatesAsync(req.Url, fetcher, translator);
                    return Results.Json(new { candidates });
                }
            )
            .RequireAuthorization();

        app.MapPost(
                "/api/collect/batch",
                async (BatchRequest req, ICollectionStore store) =>
                {
                    var items = (req.Items ?? [])
                        .Where(i => !string.IsNullOrWhiteSpace(i.Url))
                        .Select(i => new CollectionItem(
                            "",
                            i.Url,
                            i.Title ?? "",
                            i.Translation ?? "",
                            null,
                            false,
                            ""
                        ));
                    var saved = await store.AddBatchAsync(items);
                    return Results.Json(new { saved });
                }
            )
            .RequireAuthorization();

        app.MapGet(
                "/api/collections",
                async (
                    ICollectionStore store,
                    int page = 1,
                    int pageSize = 20,
                    string? search = null
                ) =>
                {
                    page = Math.Max(1, page);
                    pageSize = Math.Clamp(pageSize, 1, 100);
                    var (items, total) = await store.ListAsync(page, pageSize, search);
                    return Results.Json(
                        new
                        {
                            items,
                            total,
                            page,
                            pageSize,
                        }
                    );
                }
            )
            .RequireAuthorization();

        app.MapDelete(
                "/api/collections/{id}",
                async (string id, ICollectionStore store) =>
                {
                    var deleted = await store.DeleteAsync(id);
                    return deleted
                        ? Results.Json(new { ok = true })
                        : Results.NotFound(new { error = "not found" });
                }
            )
            .RequireAuthorization();
    }

    private static async Task<List<CandidateItem>> ExtractCandidatesAsync(
        string url,
        PageFetcher fetcher,
        TranslateService translator
    )
    {
        var (_, _, links) = await fetcher.FetchAsync(url, true);
        var candidates = new List<CandidateItem>(links.Count);
        var gate = new object();

        await Parallel.ForEachAsync(
            links,
            new ParallelOptions { MaxDegreeOfParallelism = 5 },
            async (link, ct) =>
            {
                var title = link.Title;
                var isFallback = false;
                if (string.IsNullOrWhiteSpace(title))
                {
                    try
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        cts.CancelAfter(TimeSpan.FromSeconds(10));
                        var (t, fb, _) = await fetcher.FetchAsync(link.Url, false, cts.Token);
                        title = t;
                        isFallback = fb;
                    }
                    catch (Exception)
                    {
                        title = link.Url;
                    }
                }

                var translation = await translator.TranslateAsync(title);
                lock (gate)
                {
                    candidates.Add(new CandidateItem(link.Url, title, translation, isFallback));
                }
            }
        );

        return candidates;
    }
}
