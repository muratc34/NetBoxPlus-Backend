using Billing.Domain.Plans;
using Shared.Application.Validation;

namespace Billing.Application.Plans;

/// <summary>A plan's text in one language, as an admin sends it.</summary>
public sealed record PlanTextFields(string? Name, string? Description);

/// <summary>Everything an admin sets on a plan.</summary>
public sealed record PlanFields(
    IReadOnlyDictionary<string, PlanTextFields> Translations,
    long AmountMinor,
    string? Currency,
    VideoQuality Quality,
    int MaxScreens,
    string? ProviderPriceId)
{
    internal IReadOnlyDictionary<string, PlanText> Texts
        => Translations.ToDictionary(item => item.Key, item => new PlanText(item.Value?.Name, item.Value?.Description), StringComparer.Ordinal);

    internal PlanTerms Terms => new(AmountMinor, Currency, Quality, MaxScreens, ProviderPriceId);
}

internal static class LanguageValidation
{
    public static IEnumerable<ValidationFailure> Validate(string language)
    {
        if (!Languages.IsSupported(language))
        {
            yield return new ValidationFailure("lang", PlanErrors.UnsupportedLanguage.Code, PlanErrors.UnsupportedLanguage.Message);
        }
    }
}
