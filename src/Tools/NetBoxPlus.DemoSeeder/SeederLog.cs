using Microsoft.Extensions.Logging;

namespace NetBoxPlus.DemoSeeder;

internal static partial class SeederLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Signed in to {Gateway} as the administrator")]
    public static partial void SignedIn(ILogger logger, Uri gateway);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} '{Name}' created")]
    public static partial void Created(ILogger logger, string kind, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} '{Name}': {Step}")]
    public static partial void Step(ILogger logger, string kind, string name, string step);

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for {Count} videos to be transcoded")]
    public static partial void WaitingForVideos(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Ready} of {Count} videos ready")]
    public static partial void VideosProgress(ILogger logger, int ready, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "No Stripe key; the subscription plans are skipped")]
    public static partial void PlansSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Plan '{Name}' uses Stripe price {PriceId}")]
    public static partial void PlanPrice(ILogger logger, string name, string priceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Done: {Genres} genres, {People} people, {Movies} movies, {Series} series ({Changes} changes)")]
    public static partial void Done(ILogger logger, int genres, int people, int movies, int series, int changes);

    [LoggerMessage(Level = LogLevel.Error, Message = "Seeding failed; run it again to continue where it stopped")]
    public static partial void Failed(ILogger logger, Exception exception);
}
