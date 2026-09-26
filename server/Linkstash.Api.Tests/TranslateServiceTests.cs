using Linkstash.Api.Translate;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Linkstash.Api.Tests;

public class TranslateServiceTests
{
    private static TranslateOptions Options(string email = "") => new() { ContactEmail = email };

    private static TranslateService SutForResponse(string response, string email = "")
    {
        var apiMock = new Mock<IMyMemoryApi>();
        apiMock.Setup(a => a.Get(It.IsAny<MyMemoryQuery>())).ReturnsAsync(response);
        return new TranslateService(
            apiMock.Object,
            Options(email),
            NullLogger<TranslateService>.Instance
        );
    }

    private static TranslateService SutThrowing()
    {
        var apiMock = new Mock<IMyMemoryApi>();
        apiMock
            .Setup(a => a.Get(It.IsAny<MyMemoryQuery>()))
            .ThrowsAsync(new HttpRequestException("network down"));
        return new TranslateService(
            apiMock.Object,
            Options(),
            NullLogger<TranslateService>.Instance
        );
    }

    [Fact]
    public async Task TranslateAsync_ValidResponse_ReturnsTranslation()
    {
        var sut = SutForResponse(
            """
            {"responseData":{"translatedText":"示例域","match":0.85},"responseStatus":200,"quotaFinished":false}
            """
        );
        Assert.Equal("示例域", await sut.TranslateAsync("Example Domain"));
    }

    [Fact]
    public async Task TranslateAsync_EnglishTitle_UsesEnglishSourcePair()
    {
        MyMemoryQuery? captured = null;
        var apiMock = new Mock<IMyMemoryApi>();
        apiMock
            .Setup(a => a.Get(It.IsAny<MyMemoryQuery>()))
            .Callback<MyMemoryQuery>(q => captured = q)
            .ReturnsAsync(
                """{"responseData":{"translatedText":"构建个人知识库的指南"},"responseStatus":200,"quotaFinished":false}"""
            );

        var sut = new TranslateService(
            apiMock.Object,
            Options(),
            NullLogger<TranslateService>.Instance
        );
        var result = await sut.TranslateAsync("A guide to building a personal knowledge base");

        Assert.Equal("构建个人知识库的指南", result);
        Assert.Equal("en|zh-CN", captured!.LangPair);
    }

    [Fact]
    public async Task TranslateAsync_JapaneseTitle_DetectsJapaneseSource()
    {
        MyMemoryQuery? captured = null;
        var apiMock = new Mock<IMyMemoryApi>();
        apiMock
            .Setup(a => a.Get(It.IsAny<MyMemoryQuery>()))
            .Callback<MyMemoryQuery>(q => captured = q)
            .ReturnsAsync(
                """{"responseData":{"translatedText":"测试文章标题"},"responseStatus":200,"quotaFinished":false}"""
            );

        var sut = new TranslateService(
            apiMock.Object,
            Options(),
            NullLogger<TranslateService>.Instance
        );
        var result = await sut.TranslateAsync("日本語のテスト記事");

        Assert.Equal("测试文章标题", result);
        Assert.Equal("ja|zh-CN", captured!.LangPair);
    }

    [Fact]
    public async Task TranslateAsync_ChineseTitle_SkipsApiAndReturnsOriginal()
    {
        var apiMock = new Mock<IMyMemoryApi>();
        apiMock
            .Setup(a => a.Get(It.IsAny<MyMemoryQuery>()))
            .ThrowsAsync(new InvalidOperationException("API must not be called for Chinese input"));

        var sut = new TranslateService(
            apiMock.Object,
            Options(),
            NullLogger<TranslateService>.Instance
        );
        Assert.Equal("中文标题", await sut.TranslateAsync("中文标题"));
    }

    [Fact]
    public async Task TranslateAsync_OverlyLongTitle_TruncatesTo500Chars()
    {
        MyMemoryQuery? captured = null;
        var apiMock = new Mock<IMyMemoryApi>();
        apiMock
            .Setup(a => a.Get(It.IsAny<MyMemoryQuery>()))
            .Callback<MyMemoryQuery>(q => captured = q)
            .ReturnsAsync(
                """{"responseData":{"translatedText":"截断后的译文"},"responseStatus":200,"quotaFinished":false}"""
            );

        var sut = new TranslateService(
            apiMock.Object,
            Options(),
            NullLogger<TranslateService>.Instance
        );
        var longTitle = new string('A', 900);
        var result = await sut.TranslateAsync(longTitle);

        Assert.Equal("截断后的译文", result);
        Assert.Equal(500, captured!.Text!.Length);
    }

    [Fact]
    public async Task TranslateAsync_QuotaExhausted_ReturnsOriginalWithFailureMarker()
    {
        var sut = SutForResponse(
            """{"responseStatus":200,"quotaFinished":true,"responseData":{"translatedText":""}}"""
        );
        var result = await sut.TranslateAsync("Example Domain");
        Assert.StartsWith("(翻译失败)", result);
    }

    [Fact]
    public async Task TranslateAsync_ApiErrorStatus_ReturnsOriginalWithFailureMarker()
    {
        var sut = SutForResponse(
            """{"responseStatus":403,"responseDetails":"QUERY LENGTH LIMIT EXCEEDED"}"""
        );
        var result = await sut.TranslateAsync("Example Domain");
        Assert.StartsWith("(翻译失败)", result);
    }

    [Fact]
    public async Task TranslateAsync_ApiThrows_ReturnsFailureMarker()
    {
        var sut = SutThrowing();
        var result = await sut.TranslateAsync("Example Domain");
        Assert.StartsWith("(翻译失败)", result);
        Assert.Contains("Example Domain", result);
    }

    [Fact]
    public async Task TranslateAsync_EmptyInput_ReturnsEmpty()
    {
        var sut = SutForResponse("ignored");
        Assert.Equal("", await sut.TranslateAsync(""));
        Assert.Equal("", await sut.TranslateAsync("   "));
    }

    [Fact]
    public async Task TranslateAsync_ContactEmailConfigured_SendsDeParameter()
    {
        MyMemoryQuery? captured = null;
        var apiMock = new Mock<IMyMemoryApi>();
        apiMock
            .Setup(a => a.Get(It.IsAny<MyMemoryQuery>()))
            .Callback<MyMemoryQuery>(q => captured = q)
            .ReturnsAsync(
                """{"responseData":{"translatedText":"示例域"},"responseStatus":200,"quotaFinished":false}"""
            );

        var sut = new TranslateService(
            apiMock.Object,
            Options("chen19941018@live.com"),
            NullLogger<TranslateService>.Instance
        );
        await sut.TranslateAsync("Example Domain");

        Assert.Equal("chen19941018@live.com", captured!.ContactEmail);
    }

    [Fact]
    public async Task TranslateAsync_NoContactEmail_OmitsDeParameter()
    {
        MyMemoryQuery? captured = null;
        var apiMock = new Mock<IMyMemoryApi>();
        apiMock
            .Setup(a => a.Get(It.IsAny<MyMemoryQuery>()))
            .Callback<MyMemoryQuery>(q => captured = q)
            .ReturnsAsync(
                """{"responseData":{"translatedText":"示例域"},"responseStatus":200,"quotaFinished":false}"""
            );

        var sut = new TranslateService(
            apiMock.Object,
            Options(),
            NullLogger<TranslateService>.Instance
        );
        await sut.TranslateAsync("Example Domain");

        Assert.Null(captured!.ContactEmail);
    }

    [Fact]
    public async Task TranslateAsync_TransientNetworkFailure_RetriesAndSucceeds()
    {
        var calls = 0;
        var apiMock = new Mock<IMyMemoryApi>();
        apiMock
            .Setup(a => a.Get(It.IsAny<MyMemoryQuery>()))
            .ReturnsAsync(() =>
            {
                calls++;
                if (calls < 2)
                    throw new IOException("unexpected EOF");
                return """{"responseData":{"translatedText":"示例域"},"responseStatus":200,"quotaFinished":false}""";
            });

        var sut = new TranslateService(
            apiMock.Object,
            Options(),
            NullLogger<TranslateService>.Instance
        );
        var result = await sut.TranslateAsync("Example Domain");

        Assert.Equal("示例域", result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task TranslateAsync_PersistentNetworkFailure_ReturnsMarkerAfterRetries()
    {
        var calls = 0;
        var apiMock = new Mock<IMyMemoryApi>();
        apiMock
            .Setup(a => a.Get(It.IsAny<MyMemoryQuery>()))
            .Callback(() => calls++)
            .ThrowsAsync(new IOException("unexpected EOF"));

        var sut = new TranslateService(
            apiMock.Object,
            Options(),
            NullLogger<TranslateService>.Instance
        );
        var result = await sut.TranslateAsync("Example Domain");

        Assert.StartsWith("(翻译失败)", result);
        Assert.Equal(3, calls);
    }
}
