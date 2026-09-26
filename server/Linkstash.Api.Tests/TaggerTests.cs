using Linkstash.Api.Store;
using Xunit;

namespace Linkstash.Api.Tests;

public class TaggerTests
{
    [Theory]
    // 实测样本：中文译文命中
    [InlineData("fastapi/fastapi", "FastAPI 框架，高性能，易于学习", "python")]
    [InlineData("SerhiiKorniienko/bullshit-detector", "在互联网上进行智能体技能事实核查", "AI")]
    [InlineData("某项目", "一个统一的大模型 API", "AI")]
    public void AutoTag_ChineseTranslation_HitsCategory(string title, string zh, string expected)
    {
        var tags = Tagger.AutoTag(title, zh);
        Assert.Contains(expected, tags);
    }

    [Fact]
    public void AutoTag_EnglishTitle_HitsCategory()
    {
        Assert.Contains("python", Tagger.AutoTag("tiangolo/fastapi", ""));
        Assert.Contains("rust", Tagger.AutoTag("A tokio-based runtime", ""));
        Assert.Contains("运维", Tagger.AutoTag("A Kubernetes operator", ""));
    }

    [Fact]
    public void AutoTag_RespectsWordBoundary()
    {
        // "design" 不应命中 "designer" 之外的长词；"zig" 不应命中 "zigzag"
        Assert.DoesNotContain("#熊掌记/zig", Tagger.AutoTag("A zigzag pattern library", ""));
    }

    [Fact]
    public void AutoTag_NoMatch_ReturnsEmpty()
    {
        Assert.Equal("", Tagger.AutoTag("完全无关的标题", "完全无关的描述"));
    }

    [Fact]
    public void AutoTag_LimitsToMaxTags()
    {
        // 标题堆叠多个类别关键词
        var tags = Tagger.AutoTag("python rust database cache queue", "");
        Assert.True(
            tags.Split(',').Length <= Tagger.MaxTags,
            $"实际 {tags.Split(',').Length} 个标签，超过上限 {Tagger.MaxTags}"
        );
    }

    [Fact]
    public void AutoTag_UsesHashTagFormat()
    {
        var tags = Tagger.AutoTag("tiangolo/fastapi", "");
        Assert.StartsWith("#熊掌记/", tags);
    }

    [Theory]
    [InlineData("前端", "#熊掌记/前端")]
    [InlineData("#前端", "#熊掌记/前端")]
    [InlineData("前端,设计", "#熊掌记/前端,#熊掌记/设计")]
    [InlineData("前端， 设计", "#熊掌记/前端,#熊掌记/设计")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_AddsPrefixAndDeduplicates(string? input, string expected) =>
        Assert.Equal(expected, Tagger.Normalize(input));

    [Fact]
    public void Normalize_Deduplicates()
    {
        Assert.Equal("#熊掌记/前端", Tagger.Normalize("前端,前端,#熊掌记/前端"));
    }

    [Fact]
    public void Normalize_RespectsMaxTags()
    {
        var result = Tagger.Normalize("前端,设计,数据库,算法");
        Assert.Equal(Tagger.MaxTags, result.Split(',').Length);
    }

    [Fact]
    public void Categories_ContainsUserSpecifiedSet()
    {
        foreach (
            var expected in new[]
            {
                "创业", "csharp", "python", "运维", "爬虫", "客户端", "数据库", "算法",
                "金融", "检索", "测试", "游戏", "云服务", "rust", "zig", "p2p", "学习",
                "前端", "设计", "图像识别",
            }
        )
        {
            Assert.Contains(expected, Tagger.Categories);
        }
    }
}
