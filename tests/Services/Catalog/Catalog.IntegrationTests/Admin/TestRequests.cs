using Catalog.API.Admin;
using Catalog.Application.Content;

namespace Catalog.IntegrationTests.Admin;

/// <summary>Request bodies with their text in Turkish only, for tests where the language is beside the point.</summary>
internal static class TestRequests
{
    public static Dictionary<string, ContentTextFields> Text(string? title, string? synopsis)
        => new() { ["tr"] = new ContentTextFields(title, synopsis) };

    public static GenreRequest Genre(string? name) => new(new Dictionary<string, string?> { ["tr"] = name });

    public static PersonRequest Person(string? name, string? biography)
        => new(name, biography is null ? null : new Dictionary<string, string?> { ["tr"] = biography });

    public static SeasonRequest Season(int number, string? title, int version)
        => new(number, title is null ? null : new Dictionary<string, string?> { ["tr"] = title }, version);

    public static EpisodeRequest Episode(int number, string? title, string? synopsis, int runtimeMinutes, int version)
        => new(number, Text(title, synopsis), runtimeMinutes, version);
}
