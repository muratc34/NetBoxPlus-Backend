using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catalog.Application;

/// <summary>How a request for recommendations ended, as the <c>result</c> tag spells it.</summary>
public static class RecommendationResults
{
    public const string Found = "found";

    public const string Empty = "empty";

    /// <summary>The search engine could not answer.</summary>
    public const string Unavailable = "unavailable";
}

/// <summary>The <c>NetBoxPlus.Catalog</c> meter. Tags carry kinds and outcomes, never an id.</summary>
public sealed class CatalogMetrics
{
    public const string MeterName = "NetBoxPlus.Catalog";

    private readonly Counter<long> _recommendations;

    public CatalogMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);

        _recommendations = meter.CreateCounter<long>(
            "netboxplus.catalog.recommendations",
            "{request}",
            "Recommendation requests by kind ('similar' to one title, 'recommended' from a profile's taste) and result.");
    }

    public void Recommended(string kind, string result)
        => _recommendations.Add(1, new TagList { { "kind", kind }, { "result", result } });
}
