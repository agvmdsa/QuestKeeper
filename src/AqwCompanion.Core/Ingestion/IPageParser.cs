namespace AqwCompanion.Core.Ingestion;

/// <summary>Pure function from a captured page to a typed result. Layout drift must yield Suspicious/Failed, not exceptions.</summary>
public interface IPageParser<T>
{
    string Name { get; }
    string Version { get; }
    bool CanParse(PageSnapshot snapshot);
    ParseResult<T> Parse(PageSnapshot snapshot);
}
