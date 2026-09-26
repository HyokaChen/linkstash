using Refit;

namespace Linkstash.Api.Translate;

public class MyMemoryQuery
{
    [AliasAs("q")]
    public string? Text { get; set; }

    [AliasAs("langpair")]
    public string? LangPair { get; set; }

    /// <summary>
    /// 联系邮箱。MyMemory 官方说明：提供有效邮箱可将匿名额度从 5000 提升到 50000 字符/天。
    /// 为 null 时 Refit 会省略该参数，退回匿名额度。
    /// </summary>
    [AliasAs("de")]
    public string? ContactEmail { get; set; }
}

public interface IMyMemoryApi
{
    // https://api.mymemory.translated.net/get?q=Example+Domain&langpair=en|zh-CN
    [Get("/get")]
    public Task<string> Get(MyMemoryQuery query);
}
