using System.Collections.Frozen;

namespace Catalog.Domain.Content;

/// <summary>
/// The languages content can be written in. Adding one here also needs its analyzer in the search
/// index. Turkish is the language everything must exist in and falls back to.
/// </summary>
public static class Languages
{
    public const string Turkish = "tr";
    public const string English = "en";
    public const string Default = Turkish;

    public static IReadOnlySet<string> All { get; } = new[] { Turkish, English }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsSupported(string? language) => language is not null && All.Contains(language);
}
