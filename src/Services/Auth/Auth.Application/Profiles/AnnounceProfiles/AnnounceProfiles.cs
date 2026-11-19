using Auth.Application.Abstractions;
using Auth.Contracts;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Events;
using Shared.Domain;

namespace Auth.Application.Profiles.AnnounceProfiles;

/// <summary>
/// Publishes every profile as it is now, as <see cref="ProfileChanged"/> with its current revision:
/// fills a consumer that started after the profiles were made. A consumer that already has that
/// revision ignores it. Run by the migrator on request, not by people.
/// </summary>
public sealed record AnnounceProfilesCommand : ICommand<int>;

internal sealed class AnnounceProfilesHandler(IAuthReadModel readModel, IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : ICommandHandler<AnnounceProfilesCommand, int>
{
    public async Task<Result<int>> Handle(AnnounceProfilesCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var profiles = await readModel.Users
            .SelectMany(user => user.Profiles)
            .Select(profile => new { profile.UserId, profile.Id, profile.MaxAgeRating, profile.Revision })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var profile in profiles)
        {
            await publisher.PublishAsync(
                new ProfileChanged(profile.UserId, profile.Id, profile.MaxAgeRating.ToString(), profile.Revision, now),
                cancellationToken).ConfigureAwait(false);
        }

        return profiles.Count;
    }
}
