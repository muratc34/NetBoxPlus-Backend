using Auth.Contracts;
using Billing.Contracts;
using Catalog.Contracts;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;
using Streaming.Domain.Access;

namespace Streaming.Application.Access;

// What playback decides on, copied from the services that own it, so that playing never waits for
// them. The full type name of each handler is its inbox key: renaming or moving one would process
// the messages it already handled again.

internal static class AccessEventErrors
{
    public static Error Unknown(string field, string value)
        => Error.Validation("Access.UnknownValue", $"The {field} '{value}' is not known here.");

    public static Result<TEnum> Parse<TEnum>(string field, string value)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : Unknown(field, value);
}

/// <summary>Subscriptions, from the billing service.</summary>
internal sealed class FollowSubscriptions(IDispatcher dispatcher)
    : IIntegrationEventHandler<SubscriptionActivated>, IIntegrationEventHandler<SubscriptionSuspended>, IIntegrationEventHandler<SubscriptionCanceled>
{
    public Task<Result> Handle(SubscriptionActivated integrationEvent, CancellationToken cancellationToken)
    {
        var quality = AccessEventErrors.Parse<VideoQuality>("video quality", integrationEvent.VideoQuality);
        return quality.IsFailure
            ? Task.FromResult(Result.Failure(quality.Error))
            : dispatcher.Send(
                new ApplySubscriptionCommand(
                    integrationEvent.SubscriptionId,
                    integrationEvent.UserId,
                    SubscriptionStatus.Active,
                    quality.Value,
                    integrationEvent.MaxScreens,
                    integrationEvent.Revision),
                cancellationToken);
    }

    public Task<Result> Handle(SubscriptionSuspended integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(
            new ApplySubscriptionCommand(integrationEvent.SubscriptionId, integrationEvent.UserId, SubscriptionStatus.Suspended, null, null, integrationEvent.Revision),
            cancellationToken);

    public Task<Result> Handle(SubscriptionCanceled integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(
            new ApplySubscriptionCommand(integrationEvent.SubscriptionId, integrationEvent.UserId, SubscriptionStatus.Canceled, null, null, integrationEvent.Revision),
            cancellationToken);
}

/// <summary>Viewer profiles, from the account service.</summary>
internal sealed class FollowProfiles(IDispatcher dispatcher)
    : IIntegrationEventHandler<ProfileCreated>, IIntegrationEventHandler<ProfileChanged>, IIntegrationEventHandler<ProfileRemoved>
{
    public Task<Result> Handle(ProfileCreated integrationEvent, CancellationToken cancellationToken)
        => Apply(integrationEvent.ProfileId, integrationEvent.UserId, integrationEvent.MaxAgeRating, integrationEvent.Revision, cancellationToken);

    public Task<Result> Handle(ProfileChanged integrationEvent, CancellationToken cancellationToken)
        => Apply(integrationEvent.ProfileId, integrationEvent.UserId, integrationEvent.MaxAgeRating, integrationEvent.Revision, cancellationToken);

    public Task<Result> Handle(ProfileRemoved integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new ApplyProfileCommand(integrationEvent.ProfileId, integrationEvent.UserId, null, integrationEvent.Revision), cancellationToken);

    private Task<Result> Apply(Guid profileId, Guid userId, string maxAgeRating, int revision, CancellationToken cancellationToken)
    {
        var rating = AccessEventErrors.Parse<AgeRating>("age rating", maxAgeRating);
        return rating.IsFailure
            ? Task.FromResult(Result.Failure(rating.Error))
            : dispatcher.Send(new ApplyProfileCommand(profileId, userId, rating.Value, revision), cancellationToken);
    }
}

/// <summary>What can be watched, from the catalog.</summary>
internal sealed class FollowPlayables(IDispatcher dispatcher) : IIntegrationEventHandler<PlayableChanged>
{
    public Task<Result> Handle(PlayableChanged integrationEvent, CancellationToken cancellationToken)
    {
        var kind = AccessEventErrors.Parse<TitleKind>("title kind", integrationEvent.Kind);
        if (kind.IsFailure)
        {
            return Task.FromResult(Result.Failure(kind.Error));
        }

        var rating = AccessEventErrors.Parse<AgeRating>("age rating", integrationEvent.AgeRating);
        if (rating.IsFailure)
        {
            return Task.FromResult(Result.Failure(rating.Error));
        }

        var items = integrationEvent.Items.Select(item => new Domain.Access.PlayableItem(item.ContentId, item.VideoId)).ToList();
        return dispatcher.Send(new ApplyPlayableCommand(integrationEvent.TitleId, kind.Value, rating.Value, items, integrationEvent.Revision), cancellationToken);
    }
}
