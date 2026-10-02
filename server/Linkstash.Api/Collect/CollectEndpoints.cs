using Linkstash.Api.Collect;
using Linkstash.Api.Fetch;
using Linkstash.Api.Resolve;
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
                        // 始终解析结构化条目：单页收藏顺带告诉前端"本页含 N 个条目"，
                        // 复用同一份已抓取的 DOM，不产生额外请求。
                        var fetched = await fetcher.FetchAsync(req.Url, true);

                        // 描述优先：og:description/meta description 常是真正的简介
                        // （GitHub 仓库简介即在此），比标题信息量大；缺失时退回标题。
                        var source = string.IsNullOrWhiteSpace(fetched.Description)
                            ? fetched.Title
                            : fetched.Description;
                        var translation = await translator.TranslateAsync(source);

                        var item = await store.AddAsync(
                            req.Url,
                            fetched.Title,
                            translation,
                            null,
                            fetched.IsFallback,
                            Tagger.AutoTag(fetched.Title, translation)
                        );
                        return Results.Json(
                            new CollectResult(
                                item.Id,
                                item.Url,
                                item.Title,
                                item.Translation,
                                item.IsFallbackTitle,
                                item.CreatedAt,
                                $"[{item.Title}]({item.Url}) => {item.Translation}",
                                fetched.Products.Count,
                                item.Tags
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
                    var picked = (req.Items ?? [])
                        .Where(i => !string.IsNullOrWhiteSpace(i.Url))
                        .ToList();
                    if (picked.Count == 0)
                        return Results.BadRequest(new { error = "没有可保存的条目" });

                    // 复用调用方指定的 group；否则按 GroupName 新建一个。
                    string? groupId = picked.FirstOrDefault(i => !string.IsNullOrEmpty(i.GroupId))?.GroupId;
                    if (groupId is null && !string.IsNullOrWhiteSpace(req.GroupName))
                    {
                        var group = await store.CreateGroupAsync(req.GroupName!, req.SourceUrl);
                        groupId = group.Id;
                    }

                    var items = picked
                        .Select(i =>
                        {
                            var title = i.Title ?? "";
                            var translation = i.Translation ?? "";
                            return new CollectionItem(
                                "",
                                i.Url,
                                title,
                                translation,
                                groupId is null ? null : req.SourceUrl,
                                false,
                                "",
                                Tagger.AutoTag(title, translation),
                                i.GroupId ?? groupId
                            );
                        })
                        .ToList();

                    var saved = await store.AddBatchAsync(items);
                    return Results.Json(new { saved, groupId });
                }
            )
            .RequireAuthorization();

        app.MapGet(
                "/api/collections",
                async (
                    ICollectionStore store,
                    int page = 1,
                    int pageSize = 20,
                    string? search = null,
                    string? tag = null
                ) =>
                {
                    page = Math.Max(1, page);
                    pageSize = Math.Clamp(pageSize, 1, 100);
                    var (items, total) = await store.ListAsync(page, pageSize, search, tag);
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

        // 可用标签全集，供前端渲染标签选择器
        app.MapGet("/api/tags", () => Results.Json(new { categories = Store.Tagger.Categories }))
            .RequireAuthorization();

        // 手动改标签
        app.MapPut(
                "/api/collections/{id}/tags",
                async (string id, TagsRequest req, ICollectionStore store) =>
                {
                    var updated = await store.UpdateTagsAsync(id, Store.Tagger.Normalize(req.Tags));
                    return updated is null
                        ? Results.NotFound(new { error = "not found" })
                        : Results.Json(new { tags = updated.Tags });
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

        // 统一解析入口：完整 URL / owner-repo slug / 关键词 混合粘贴都走这里。
        app.MapPost(
                "/api/collect/resolve",
                async (
                    ResolveRequest req,
                    PageFetcher fetcher,
                    TranslateService translator,
                    SlugResolver slugResolver,
                    SearchResolver searchResolver,
                    ICollectionStore store
                ) =>
                {
                    var inputs = InputResolver.Resolve(req.Input);
                    if (inputs.Count == 0)
 {
                        return Results.BadRequest(new { error = "未识别到有效内容" });
                    }

                    // 每条输入 → 一个或多个候选 URL；解析不到时记录原文供前端提示。
                    var urlToInput = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var unresolved = new List<string>();

                    foreach (var input in inputs)
                    {
                        switch (input.Kind)
                        {
                            case InputKind.Url:
                                if (UrlNormalizer.Normalize(input.Url) is { } u)
                                    urlToInput[u] = input.Raw;
                                else
                                    unresolved.Add(input.Raw);
                                break;

                            case InputKind.Slug:
 {
                                var slugUrl = await slugResolver.TryResolveRepoAsync(input.Raw);
                                if (slugUrl is not null)
                                {
                                    if (UrlNormalizer.Normalize(slugUrl) is { } ru)
                                        urlToInput[ru] = input.Raw;
                                }
                                else
                                {
                                    // 仓库不存在，退化为检索
                                    await AddSearchResultsAsync(
                                        $"{input.Raw} github",
                                        urlToInput,
                                        unresolved,
                                        searchResolver
                                    );
                                }
                                break;
                            }

                            default:
                                await AddSearchResultsAsync(
                                    input.Raw,
                                    urlToInput,
                                    unresolved,
                                    searchResolver
                                );
                                break;
                        }
                    }

                    if (urlToInput.Count == 0)
                    {
                        return Results.BadRequest(
                            new
                            {
                                error = "未能解析出任何网址，请确认输入或手动补全完整 URL",
                                unresolved,
                            }
                        );
                    }

                    // 剔除已收藏
                    var normalizedUrls = urlToInput.Keys.ToList();
                    var existing = await store.ExistingNormalizedUrlsAsync(normalizedUrls);

                    var targets = urlToInput
                        .Where(kv => !existing.Contains(kv.Key))
                        .Select(kv => kv.Key)
                        .ToList();

                    var resolved = new List<ResolvedCandidate>(targets.Count);
                    var gate = new object();

                    await Parallel.ForEachAsync(
                        targets,
                        new ParallelOptions { MaxDegreeOfParallelism = 3 },
                        async (url, ct) =>
                        {
                            var title = url;
                            string? translation = null;
                            var isFallback = true;
                            try
                            {
                                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                cts.CancelAfter(TimeSpan.FromSeconds(15));
                                var fetched = await fetcher.FetchAsync(url, false, cts.Token);
                                title = fetched.Title;
                                isFallback = fetched.IsFallback;
                                var source = string.IsNullOrWhiteSpace(fetched.Description)
                                    ? title
                                    : fetched.Description;
                                translation = await translator.TranslateAsync(source);
                            }
                            catch (Exception)
                            {
                                translation = title;
                            }

                            var candidate = new ResolvedCandidate(
                                urlToInput[url],
                                url,
                                title,
                                translation ?? title,
                                isFallback,
                                Tagger.AutoTag(title, translation ?? ""),
                                false
                            );
                            lock (gate)
                                resolved.Add(candidate);
                        }
                    );

                    // 已存在的条目也回传，让前端能显示"已收藏，跳过"
                    foreach (var kv in urlToInput.Where(k => existing.Contains(k.Key)))
 {
                        resolved.Add(
                            new ResolvedCandidate(kv.Value, kv.Key, kv.Key, "", true, "", true)
                        );
                    }

                    return Results.Json(new { resolved, unresolved });
                }
            )
            .RequireAuthorization();
    }

    private static async Task AddSearchResultsAsync(
        string query,
        Dictionary<string, string> urlToInput,
        List<string> unresolved,
        SearchResolver searchResolver
    )
    {
        var hits = await searchResolver.SearchAsync(query);
        if (hits.Count == 0)
        {
            unresolved.Add(query);
            return;
        }

        foreach (var hit in hits)
        {
            if (UrlNormalizer.Normalize(hit.Url) is { } norm)
                urlToInput.TryAdd(norm, query);
        }
    }

    private static async Task<List<CandidateItem>> ExtractCandidatesAsync(
        string url,
        PageFetcher fetcher,
        TranslateService translator
    )
    {
        var fetched = await fetcher.FetchAsync(url, true);

        // 优先使用结构化条目（产品名称/介绍/URL 自带），无需逐个抓取目标页；
        // 介绍通常已是中文，TranslateService 会自动跳过翻译。
        if (fetched.Products.Count > 0)
            return
            [
                .. fetched.Products.Select(p => new CandidateItem(
                    p.Url,
                    p.Name,
                    p.Description,
                    false
                )),
            ];

        var links = fetched.Links;
        var candidates = new List<CandidateItem>(links.Count);
        var gate = new object();

        await Parallel.ForEachAsync(
            links,
            new ParallelOptions { MaxDegreeOfParallelism = 5 },
            async (link, ct) =>
            {
                // 聚合页里的链接文本常是裸 URL，几乎总需回源取标题/描述。
                var title = link.Title;
                string? description = null;
                var isFallback = false;
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(10));
                    var target = await fetcher.FetchAsync(link.Url, false, cts.Token);
                    title = target.Title;
                    description = target.Description;
                    isFallback = target.IsFallback;
                }
                catch (Exception)
                {
                    title = string.IsNullOrWhiteSpace(title) ? link.Url : title;
                }

                // 优先翻译描述（信息量大于标题），缺失才退回标题。
                var source = string.IsNullOrWhiteSpace(description) ? title : description!;
                var translation = await translator.TranslateAsync(source);
                lock (gate)
                {
                    candidates.Add(new CandidateItem(link.Url, title, translation, isFallback));
                }
            }
        );

        return candidates;
    }
}
