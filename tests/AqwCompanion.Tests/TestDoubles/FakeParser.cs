using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeParser<T>(Func<PageSnapshot, ParseResult<T>> parse) : IPageParser<T>
{
    public string Name => "fake";
    public string Version => "1";
    public bool CanParse(PageSnapshot snapshot) => true;
    public ParseResult<T> Parse(PageSnapshot snapshot) => parse(snapshot);
}
