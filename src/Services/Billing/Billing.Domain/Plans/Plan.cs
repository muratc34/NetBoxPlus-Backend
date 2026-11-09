using System.Collections.Frozen;
using Shared.Domain;

namespace Billing.Domain.Plans;

public enum VideoQuality
{
    Sd,
    Hd,
    Uhd,
}

/// <summary>The text of a plan in one language, as given by an admin.</summary>
public sealed record PlanText(string? Name, string? Description);

/// <summary>What a plan offers and costs, as given by an admin.</summary>
public sealed record PlanTerms(long AmountMinor, string? Currency, VideoQuality Quality, int MaxScreens, string? ProviderPriceId);

/// <summary>A plan's text in one language. Part of the <see cref="Plan"/> aggregate.</summary>
public sealed class PlanTranslation
{
    internal PlanTranslation(string language, string name, string description)
    {
        Language = language;
        Name = name;
        Description = description;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private PlanTranslation()
    {
    }

    public string Language { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    internal void Change(string name, string description)
    {
        Name = name;
        Description = description;
    }
}

/// <summary>
/// A monthly subscription plan. Only active plans can be bought; deactivating one leaves existing
/// subscriptions alone. The price is in minor units, so that no amount is ever rounded.
/// </summary>
public sealed class Plan : AggregateRoot, IAuditableEntity
{
    public const int NameMaxLength = 64;
    public const int DescriptionMaxLength = 500;
    public const int ProviderPriceIdMaxLength = 128;
    public const int CurrencyLength = 3;
    public const int MaxScreensLimit = 10;

    private static readonly IReadOnlySet<string> Currencies = new[] { "TRY" }.ToFrozenSet(StringComparer.Ordinal);

    private readonly List<PlanTranslation> _translations = [];

    private Plan(Guid id)
        : base(id)
    {
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Plan()
    {
    }

    public IReadOnlyCollection<PlanTranslation> Translations => _translations.AsReadOnly();

    public long AmountMinor { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public VideoQuality Quality { get; private set; }

    public int MaxScreens { get; private set; }

    /// <summary>The payment provider's price this plan is sold at.</summary>
    public string ProviderPriceId { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    /// <summary>Goes up with every change; a client sends back the value it loaded.</summary>
    public int Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    /// <summary>A new plan, not on sale until it is activated.</summary>
    public static Result<Plan> Create(IReadOnlyDictionary<string, PlanText> texts, PlanTerms terms)
    {
        var plan = new Plan(Guid.CreateVersion7());
        var changed = plan.Change(texts, terms);
        return changed.IsFailure ? changed.Error : plan;
    }

    public Result Change(IReadOnlyDictionary<string, PlanText> texts, PlanTerms terms)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(terms);

        if (texts.Keys.Any(language => !Languages.IsSupported(language)))
        {
            return PlanErrors.UnsupportedLanguage;
        }

        if (!texts.ContainsKey(Languages.Default))
        {
            return PlanErrors.NameRequired;
        }

        var kept = new Dictionary<string, (string Name, string Description)>(StringComparer.Ordinal);
        foreach (var (language, text) in texts)
        {
            var name = text.Name?.Trim();
            var description = text.Description?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                return PlanErrors.NameRequired;
            }

            if (name.Length > NameMaxLength)
            {
                return PlanErrors.NameTooLong;
            }

            if (description.Length > DescriptionMaxLength)
            {
                return PlanErrors.DescriptionTooLong;
            }

            kept[language] = (name, description);
        }

        var priceId = terms.ProviderPriceId?.Trim();
        if (terms.AmountMinor <= 0)
        {
            return PlanErrors.AmountInvalid;
        }

        if (terms.Currency is null || !Currencies.Contains(terms.Currency))
        {
            return PlanErrors.CurrencyUnsupported;
        }

        if (!Enum.IsDefined(terms.Quality))
        {
            return PlanErrors.QualityInvalid;
        }

        if (terms.MaxScreens is < 1 or > MaxScreensLimit)
        {
            return PlanErrors.ScreensInvalid;
        }

        if (string.IsNullOrEmpty(priceId) || priceId.Length > ProviderPriceIdMaxLength)
        {
            return PlanErrors.ProviderPriceRequired;
        }

        _translations.RemoveAll(translation => !kept.ContainsKey(translation.Language));
        foreach (var (language, text) in kept)
        {
            var existing = _translations.Find(translation => translation.Language == language);
            if (existing is null)
            {
                _translations.Add(new PlanTranslation(language, text.Name, text.Description));
            }
            else
            {
                existing.Change(text.Name, text.Description);
            }
        }

        AmountMinor = terms.AmountMinor;
        Currency = terms.Currency;
        Quality = terms.Quality;
        MaxScreens = terms.MaxScreens;
        ProviderPriceId = priceId;
        Version++;
        return Result.Success();
    }

    /// <summary>The text in the language, or in the default language when it has not been translated.</summary>
    public PlanTranslation TextFor(string language)
        => _translations.Find(translation => translation.Language == language)
            ?? _translations.First(translation => translation.Language == Languages.Default);

    public void Activate()
    {
        if (!IsActive)
        {
            IsActive = true;
            Version++;
        }
    }

    public void Deactivate()
    {
        if (IsActive)
        {
            IsActive = false;
            Version++;
        }
    }
}

public static class PlanErrors
{
    public static readonly Error NotFound = Error.NotFound("Plan.NotFound", "The plan was not found.");

    public static readonly Error UnsupportedLanguage = Error.Validation("Plan.UnsupportedLanguage", "The language is not supported.");

    public static readonly Error NameRequired = Error.Validation("Plan.NameRequired", "The plan needs a name in Turkish, and a name in every language given.");

    public static readonly Error NameTooLong = Error.Validation("Plan.NameTooLong", $"The plan name must be at most {Plan.NameMaxLength} characters.");

    public static readonly Error DescriptionTooLong = Error.Validation("Plan.DescriptionTooLong", $"The description must be at most {Plan.DescriptionMaxLength} characters.");

    public static readonly Error AmountInvalid = Error.Validation("Plan.AmountInvalid", "The price must be greater than zero.");

    public static readonly Error CurrencyUnsupported = Error.Validation("Plan.CurrencyUnsupported", "The currency is not supported.");

    public static readonly Error QualityInvalid = Error.Validation("Plan.QualityInvalid", "The video quality is not valid.");

    public static readonly Error ScreensInvalid = Error.Validation("Plan.ScreensInvalid", $"The number of screens must be between 1 and {Plan.MaxScreensLimit}.");

    public static readonly Error ProviderPriceRequired = Error.Validation("Plan.ProviderPriceRequired", $"The provider price id is required, at most {Plan.ProviderPriceIdMaxLength} characters.");
}
