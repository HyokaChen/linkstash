using Linkstash.Api.Fetch;
using Linkstash.Api.Translate;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Linkstash.Api.Tests;

/// <summary>
/// 基于真实公开页面的集成测试（需网络）。
/// 覆盖两类形态：
/// 1) 结构化聚合页——微信公众号 Show HN 周报，正文用固定标签罗列条目且链接为纯文本；
/// 2) 普通单页——GitHub 仓库，描述在 og:description 且为英文，需翻译成中文。
/// 默认不参与 dotnet test，通过 LINKSTASH_LIVE_TESTS=1 显式开启，避免 CI 因网络抖动红。
/// </summary>
public class LiveFetchTests(ITestOutputHelper output)
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("LINKSTASH_LIVE_TESTS") == "1";

    private static PageFetcher Fetcher(string? proxy = null)
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(proxy))
        {
            handler.Proxy = new System.Net.WebProxy(proxy);
            handler.UseProxy = true;
        }
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36"
        );
        return new PageFetcher(http);
    }

    /// <summary>
    /// 代理链路偶发 SSL EOF/超时（实测约 1/3 概率），生产代码已重试，
    /// 此处同样重试以免把网络抖动误报为回归。
    /// </summary>
    private async Task<FetchResult> FetchWithRetry(
        PageFetcher fetcher,
        string url,
        bool extractLinks
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await fetcher.FetchAsync(url, extractLinks);
            }
            catch (Exception ex)
                when (attempt < 4
                    && ex is HttpRequestException or TaskCanceledException or IOException
                )
            {
                output.WriteLine($"  [retry {attempt}] {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(500 * attempt);
            }
        }
    }

    private static TranslateService Translator(IMyMemoryApi? api = null) =>
        new(
            api ?? new MyMemoryApiStub(),
            new TranslateOptions(),
            NullLogger<TranslateService>.Instance
        );

    private sealed class MyMemoryApiStub : IMyMemoryApi
    {
        public Task<string> Get(MyMemoryQuery query) =>
            Task.FromResult(
                $$"""{"responseData":{"translatedText":"[译]{{query.Text}}"},"responseStatus":200,"quotaFinished":false}"""
            );
    }

    [SkippableFact]
    public async Task WeChatAggregationPage_ExtractsStructuredProducts()
    {
        Skip.IfNot(Enabled, "需要网络：设置 LINKSTASH_LIVE_TESTS=1");
        var fetcher = Fetcher(Environment.GetEnvironmentVariable("LINKSTASH_PROXY"));

        var result = await FetchWithRetry(
            fetcher,
            "https://mp.weixin.qq.com/s/CWGNv8J2we8K35cFnO7DgQ",
            extractLinks: true
        );

        // 标题：微信 <title> 为空，应回退到 og:title
        output.WriteLine($"title       = {result.Title}");
        output.WriteLine($"description = {result.Description}");
        output.WriteLine($"links       = {result.Links.Count}");
        output.WriteLine($"products    = {result.Products.Count}");

        Assert.False(result.IsFallback, "og:title 存在，不应走 URL 兜底");
        Assert.Contains("Whiteboard", result.Title);
        Assert.NotEmpty(result.Products);

        var whiteboard = result.Products.FirstOrDefault(p => p.Name.Contains("Whiteboard"));
        Assert.NotNull(whiteboard);
        output.WriteLine(
            $"  -> {whiteboard.Name} | {whiteboard.Url} | {whiteboard.Description[..Math.Min(60, whiteboard.Description.Length)]}"
        );

        Assert.Equal("https://github.com/devdotfast/whiteboard", whiteboard.Url);
        Assert.Contains("YC W26", whiteboard.Description);
        // 产品链接必须真实存在，不能混入微信自身/图片 CDN
        Assert.All(result.Products, p => Assert.DoesNotContain("mmbiz.qpic.cn", p.Url));
    }

    [SkippableFact]
    public async Task WeChatAggregationPage_LinksArePlainTextNotHref()
    {
        Skip.IfNot(Enabled, "需要网络：设置 LINKSTASH_LIVE_TESTS=1");
        var fetcher = Fetcher(Environment.GetEnvironmentVariable("LINKSTASH_PROXY"));

        var result = await FetchWithRetry(
            fetcher,
            "https://mp.weixin.qq.com/s/CWGNv8J2we8K35cFnO7DgQ",
            extractLinks: true
        );

        // 该页正文链接写在 <span> 文本里，a[href] 为空；实现必须同时覆盖两种来源。
        Assert.Contains(result.Links, l => l.Url.Contains("news.ycombinator.com"));
    }

    [SkippableFact]
    public async Task GitHubRepo_YieldsDescriptionReadyForTranslation()
    {
        Skip.IfNot(Enabled, "需要网络：设置 LINKSTASH_LIVE_TESTS=1");
        var fetcher = Fetcher(Environment.GetEnvironmentVariable("LINKSTASH_PROXY"));

        var result = await FetchWithRetry(
            fetcher,
            "https://github.com/SerhiiKorniienko/bullshit-detector",
            extractLinks: false
        );

        output.WriteLine($"title       = {result.Title}");
        output.WriteLine($"description = {result.Description}");

        Assert.False(result.IsFallback);
        Assert.Equal("SerhiiKorniienko/bullshit-detector", result.Title);
        Assert.NotNull(result.Description);
        Assert.Equal(
            "Agent skills that fact-check the internet: claim-by-claim verification with sources "
                + "and a 0-10 BS score for any YouTube video, article, tweet, or PDF",
            result.Description
        );
        // 取 About 栏而非 og:description：后者尾部带 " - owner/repo"
        Assert.DoesNotContain("SerhiiKorniienko", result.Description);
    }

    [SkippableFact]
    public async Task GitHubRepo_TitleIsOwnerSlashRepo()
    {
        Skip.IfNot(Enabled, "需要网络：设置 LINKSTASH_LIVE_TESTS=1");
        var fetcher = Fetcher(Environment.GetEnvironmentVariable("LINKSTASH_PROXY"));

        var result = await FetchWithRetry(
            fetcher,
            "https://github.com/SerhiiKorniienko/bullshit-detector",
            extractLinks: false
        );

        // 原始 <title> 是 "GitHub - owner/repo: 描述 · GitHub"，应收敛为 owner/repo
        Assert.Equal("SerhiiKorniienko/bullshit-detector", result.Title);
        Assert.DoesNotContain("GitHub -", result.Title);
    }
}
