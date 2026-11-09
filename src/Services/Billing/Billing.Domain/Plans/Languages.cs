using System.Collections.Frozen;

namespace Billing.Domain.Plans;

/// <summary>The languages plan texts can be written in. Turkish is required and is the fallback.</summary>
public static class Languages
{
    public const string Turkish = "tr";
    public const string English = "en";
    public const string Default = Turkish;

    public static IReadOnlySet<string> All { get; } = new[] { Turkish, English }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsSupported(string? language) => language is not null && All.Contains(language);
}
