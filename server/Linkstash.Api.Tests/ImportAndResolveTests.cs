using System.Net;
using Linkstash.Api.Collect;
using Linkstash.Api.Import;
using Linkstash.Api.Resolve;
using Linkstash.Api.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Linkstash.Api.Tests;

public class InputResolverTests
{
    [Fact]
    public void Resolve_FullUrl_ClassifiedAsUrl()
    {
        var result = InputResolver.Resolve("https://example.com/a");
        Assert.Single(result);
        Assert.Equal(InputKind.Url, result[0].Kind);
        Assert.Equal("https://example.com/a", result[0].Url);
    }

    [Fact]
    public void Resolve_OwnerRepoSlug_ClassifiedAsSlug()
    {
        var result = InputResolver.Resolve("voidmuse-dev/voidmuse");
        Assert.Single(result);
        Assert.Equal(InputKind.Slug, result[0].Kind);
        Assert.Null(result[0].Url);
    }

    [Fact]
    public void Resolve_MultipleUrlsWithFullwidthComma_AllParsed()
    {
        var result = InputResolver.Resolve(
            "https://github.com/davialabs/davia，https://github.com/halo-dev/upage，https://github.com/1Panel-dev/CordysCRM，"
        );
        var urls = result.Where(r => r.Kind == InputKind.Url).Select(r => r.Url).ToList();
        Assert.Equal(3, urls.Count);
        Assert.Contains("https://github.com/davialabs/davia", urls);
        Assert.Contains("https://github.com/1Panel-dev/CordysCRM", urls);
    }

    [Fact]
    public void Resolve_SpaceSeparatedUrls_EachParsed()
    {
        var result = InputResolver.Resolve("https://github.com/a/one https://github.com/b/two");
        var urls = result.Where(r => r.Kind == InputKind.Url).ToList();
        Assert.Equal(2, urls.Count);
    }

    [Fact]
    public void Resolve_MultiWordPhrase_KeepsPhraseIntact()
    {
        // "davia labs" 不能被空格拆成两个独立检索词，否则各自都无结果
        var result = InputResolver.Resolve("davia labs");
        Assert.Contains(result, r => r.Kind == InputKind.SearchQuery && r.Raw == "davia labs");
    }

    [Fact]
    public void Resolve_BareWords_NotSilentlyDropped()
    {
        // 关键回归：无法识别的片段必须转为 SearchQuery，不能被丢弃
        var result = InputResolver.Resolve("some random words here");
        Assert.Contains(result, r => r.Kind == InputKind.SearchQuery);
        Assert.DoesNotContain(result, r => r.Kind == InputKind.Url);
    }

    [Fact]
    public void Resolve_Empty_ReturnsEmpty() => Assert.Empty(InputResolver.Resolve("   "));

    [Fact]
    public void Resolve_MixedInput_ClassifiesEachKind()
    {
        var result = InputResolver.Resolve("https://example.com/x，owner/repo，关键词搜索");
        Assert.Contains(result, r => r.Kind == InputKind.Url);
        Assert.Contains(result, r => r.Kind == InputKind.Slug);
        Assert.Contains(result, r => r.Kind == InputKind.SearchQuery);
    }
}

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("https://github.com/a/b/", "https://github.com/a/b")]
    [InlineData("https://GitHub.com/a/b", "https://github.com/a/b")]
    [InlineData("https://github.com/a/b#readme", "https://github.com/a/b")]
    [InlineData("https://github.com/a/b?tab=readme", "https://github.com/a/b?tab=readme")]
    public void Normalize_TrimsTrailingSlashLowercasesHost(string input, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(input));

    [Fact]
    public void Normalize_PathCaseIsPreserved()
    {
        // GitHub 仓库名大小写敏感，A/B 与 a/b 不是同一个仓库
        Assert.NotEqual(
            UrlNormalizer.Normalize("https://github.com/A/B"),
            UrlNormalizer.Normalize("https://github.com/a/b")
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("javascript:void(0)")]
    public void Normalize_Invalid_ReturnsNull(string? input) =>
        Assert.Null(UrlNormalizer.Normalize(input));

    [Fact]
    public void Clean_TrimsOnly() =>
        Assert.Equal("https://x/y/", UrlNormalizer.Clean("  https://x/y/  "));
}

public class BookmarkParserTests
{
    private const string Sample = """
        <!DOCTYPE NETSCAPE-Bookmark-file-1>
        <DL><p>
            <DT><H3 ADD_DATE="1">Bookmarks bar</H3>
            <DL><p>
                <DT><A HREF="https://github.com/davialabs/davia" ADD_DATE="2">davia</A>
                <DT><A HREF="https://news.ycombinator.com/" ADD_DATE="3">HN</A>
            </DL><p>
            <DT><H3 ADD_DATE="4">学习</H3>
            <DL><p>
                <DT><A HREF="https://www.python.org/" ADD_DATE="5">Python</A>
            </DL><p>
        </DL><p>
        """;

    [Fact]
    public void Parse_ExtractsAllBookmarks()
    {
        var entries = BookmarkParser.Parse(Sample);
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void Parse_AssignsFolderPath()
    {
        var entries = BookmarkParser.Parse(Sample);
        Assert.Equal("Bookmarks bar", entries.First(e => e.Title == "davia").FolderPath);
        Assert.Equal("学习", entries.First(e => e.Title == "Python").FolderPath);
    }

    [Fact]
    public void Parse_IgnoresJavascriptPlaceholders()
    {
        var html =
            """<DL><p><DT><A HREF="javascript:void(0);">x</A><DT><A HREF="https://a.com/">a</A></DL><p>""";
        var entries = BookmarkParser.Parse(html);
        Assert.Single(entries);
        Assert.Equal("https://a.com/", entries[0].Url);
    }

    [Fact]
    public void Parse_NonBookmarkHtml_ReturnsEmpty() =>
        Assert.Empty(BookmarkParser.Parse("<html><body>hello</body></html>"));

    [Fact]
    public void Parse_EmptyString_ReturnsEmpty() => Assert.Empty(BookmarkParser.Parse(""));
}

public class SearchResolverTests
{
    private const string DdgHtml = """
        <div class="result">
          <a class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fgithub.com%2Fvoidmuse-dev%2Fvoidmuse&amp;rut=abc">voidmuse</a>
        </div>
        <td class="result__snippet">An AI IDE plugin</td>
        """;

    private static HttpClient ClientReturning(string body, HttpStatusCode status)
    {
        var handler = new StubHandler(body, status);
        return new HttpClient(handler);
    }

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task SearchAsync_UnwrapsUddgAndStripsTags()
    {
        var sut = new SearchResolver(
            ClientReturning(DdgHtml, HttpStatusCode.OK),
            NullLogger<SearchResolver>.Instance
        );
        var hits = await sut.SearchAsync("voidmuse github");
        Assert.Single(hits);
        Assert.Equal("https://github.com/voidmuse-dev/voidmuse", hits[0].Url);
        Assert.Equal("voidmuse", hits[0].Title);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var sut = new SearchResolver(
            ClientReturning(DdgHtml, HttpStatusCode.OK),
            NullLogger<SearchResolver>.Instance
        );
        Assert.Empty(await sut.SearchAsync("  "));
    }

    [Fact]
    public async Task SearchAsync_NoResults_ReturnsEmptyWithoutThrowing()
    {
        var sut = new SearchResolver(
            ClientReturning("<html>nothing</html>", HttpStatusCode.OK),
            NullLogger<SearchResolver>.Instance
        );
        Assert.Empty(await sut.SearchAsync("nothing here"));
    }

    [Fact]
    public async Task SearchAsync_AcceptedButEmpty_RetriesThenReturnsEmpty()
    {
        // 实测 202 空响应是限流特征，必须重试后返回空列表而不是抛异常
        var sut = new SearchResolver(
            ClientReturning("", HttpStatusCode.Accepted),
            NullLogger<SearchResolver>.Instance
        );
        Assert.Empty(await sut.SearchAsync("davia labs"));
    }
}

public class SlugResolverTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private static HttpClient Client(HttpStatusCode status, string body)
    {
        // SlugResolver 使用相对路径 repos/{slug}，BaseAddress 由 DI 注册提供；
        // 测试里同样设置，保持与生产一致的请求形态。
        var client = new HttpClient(new StubHandler(status, body))
        {
            BaseAddress = new Uri("https://api.github.com"),
        };
        return client;
    }

    [Fact]
    public async Task TryResolveRepoAsync_Exists_ReturnsHtmlUrl()
    {
        var body =
            """{"html_url":"https://github.com/voidmuse-dev/voidmuse","full_name":"voidmuse-dev/voidmuse"}""";
        var sut = new SlugResolver(
            Client(HttpStatusCode.OK, body),
            NullLogger<SlugResolver>.Instance
        );
        Assert.Equal(
            "https://github.com/voidmuse-dev/voidmuse",
            await sut.TryResolveRepoAsync("voidmuse-dev/voidmuse")
        );
    }

    [Fact]
    public async Task TryResolveRepoAsync_NotFound_ReturnsNullWithoutThrowing()
    {
        var sut = new SlugResolver(
            Client(HttpStatusCode.NotFound, """{"message":"Not Found"}"""),
            NullLogger<SlugResolver>.Instance
        );
        Assert.Null(await sut.TryResolveRepoAsync("nonexistent/repo"));
    }
}
