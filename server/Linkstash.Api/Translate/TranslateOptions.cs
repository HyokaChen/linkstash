namespace Linkstash.Api.Translate;

public class TranslateOptions
{
    /// <summary>
    /// MyMemory 联系邮箱，用于把每日免费额度从 5000 提升到 50000 字符。
    /// 留空则使用匿名额度。生产环境建议用环境变量 Translate__ContactEmail 覆盖。
    /// </summary>
    public string ContactEmail { get; set; } = "";
}
