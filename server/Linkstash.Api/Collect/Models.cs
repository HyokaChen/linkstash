namespace Linkstash.Api.Collect;

public record CollectRequest(string Url, bool ExtractLinks = false);
public record ExtractRequest(string Url);
public record BatchRequest(IEnumerable<BatchItem> Items);
public record BatchItem(string Url, string Title, string Translation);
public record CandidateItem(string Url, string Title, string Translation, bool IsFallbackTitle);
public record CollectResult(string Id, string Url, string Title, string Translation, bool IsFallbackTitle, string CreatedAt, string Markdown);
