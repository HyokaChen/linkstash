using Linkstash.Api.Translate;
using Xunit;

namespace Linkstash.Api.Tests;

public class GlossaryTests
{
    [Theory]
    // 实测 MyMemory en→zh-CN 的错译，术语表需纠正
    [InlineData("Agent skills that fact-check the internet", "智能体技能 that fact-check the internet")]
    [InlineData("A tool for building AI agent workflows", "A tool for building AI 智能体 workflows")]
    [InlineData("Manage repo issues and commit history", "Manage 仓库 Issue and 提交 history")]
    [InlineData("Open a pull request and review code", "Open a Pull Request and review code")]
    [InlineData("A unified API for every LLM provider", "A unified API for every LLM provider")]
    [InlineData("An open source self-hosted knowledge base", "An open source self-hosted knowledge base")]
    public void Apply_ReplacesTerm(string input, string expected) =>
        Assert.Equal(expected, Glossary.Apply(input));

    [Theory]
    // 词边界：不能误伤包含目标词的更长单词
    [InlineData("A multi-agent reinforcement learning framework", "A 多智能体 reinforcement learning framework")]
    [InlineData("An agent-based modelling toolkit", "An 基于智能体的 modelling toolkit")]
    [InlineData("The reagent was stored in a bottle", "The reagent was stored in a bottle")]
    [InlineData("repository documentation", "repository documentation")]
    public void Apply_RespectsWordBoundaries(string input, string expected) =>
        Assert.Equal(expected, Glossary.Apply(input));

    [Fact]
    public void Apply_LongestTermWins()
    {
        // "agent skills" 必须先于 "agent" 命中，否则会得到 "智能体 skills"
        Assert.Equal("智能体技能 for the web", Glossary.Apply("agent skills for the web"));
    }

    [Fact]
    public void Apply_IsCaseInsensitive()
    {
        Assert.Equal("AI 智能体 and 智能体", Glossary.Apply("AI Agent and agent"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Apply_EmptyInput_ReturnedAsIs(string input) => Assert.Equal(input, Glossary.Apply(input));

    [Fact]
    public void Apply_NoTerms_LeavesTextUnchanged() =>
        Assert.Equal("A simple calculator app", Glossary.Apply("A simple calculator app"));
}
