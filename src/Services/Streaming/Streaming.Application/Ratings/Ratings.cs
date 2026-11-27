using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Application.Playback;
using Streaming.Application.Playback.GetProgress;
using Streaming.Domain.Playback;
using Streaming.Domain.Ratings;

namespace Streaming.Application.Ratings;

/// <summary>How one of the user's profiles rated a title.</summary>
public sealed record GetRatingQuery(Guid UserId, Guid ProfileId, Guid TitleId) : IQuery<TitleRatingView>;

/// <param name="Rating">The level chosen, or <see langword="null"/> when the profile has not rated the title.</param>
public sealed record TitleRatingView(RatingLevel? Rating);

/// <summary>Rates a title the profile can watch now, or changes its rating.</summary>
public sealed record RateTitleCommand(Guid UserId, Guid ProfileId, Guid TitleId, RatingLevel? Rating) : ICommand;

/// <summary>Takes back the profile's rating of a title.</summary>
public sealed record RemoveRatingCommand(Guid UserId, Guid ProfileId, Guid TitleId) : ICommand;

public static class RatingErrors
{
    public static readonly Error Invalid = Error.Validation("Rating.Invalid", "The rating must be NotForMe, Like or Love.");
}

internal sealed class GetRatingHandler(ProfileAccess profileAccess, IStreamingReadModel readModel) : IQueryHandler<GetRatingQuery, TitleRatingView>
{
    public async Task<Result<TitleRatingView>> Handle(GetRatingQuery query, CancellationToken cancellationToken)
    {
        var found = await profileAccess.FindAsync(query.UserId, query.ProfileId, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var level = await readModel.TitleRatings
            .Where(rating => rating.ProfileId == query.ProfileId && rating.TitleId == query.TitleId)
            .Select(rating => (RatingLevel?)rating.Level)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return new TitleRatingView(level);
    }
}

internal sealed class RateTitleValidator : IValidator<RateTitleCommand>
{
    public IEnumerable<ValidationFailure> Validate(RateTitleCommand instance)
    {
        if (instance.Rating is not { } level || !Enum.IsDefined(level))
        {
            yield return new ValidationFailure("rating", RatingErrors.Invalid.Code, RatingErrors.Invalid.Message);
        }
    }
}

/// <summary>
/// The same checks as "my list": the title can be watched now, by this profile; no subscription is needed.
/// </summary>
internal sealed class RateTitleHandler(ProfileAccess profileAccess, 
    IStreamingReadModel readModel,
    ITitleRatingRepository ratings,
    StreamingMetrics metrics,
    TimeProvider timeProvider) : ICommandHandler<RateTitleCommand>
{
    public async Task<Result> Handle(RateTitleCommand command, CancellationToken cancellationToken)
    {
        var found = await profileAccess.FindAsync(command.UserId, command.ProfileId, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var profile = found.Value;

        var title = await readModel.PlayableTitles
            .Where(item => item.Id == command.TitleId && item.Items.Any())
            .Select(item => new { item.AgeRating })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (title is null)
        {
            return PlaybackErrors.NotAvailable;
        }

        if (!profile.MayWatch(title.AgeRating))
        {
            return PlaybackErrors.AgeRestricted;
        }

        var level = command.Rating!.Value;
        var now = timeProvider.GetUtcNow();
        var rating = await ratings.FindAsync(command.ProfileId, command.TitleId, cancellationToken).ConfigureAwait(false);
        if (rating is null)
        {
            // Two first ratings at once are settled by the primary key: the loser retries (409 Data.Conflict).
            ratings.Add(TitleRating.Create(command.ProfileId, command.UserId, command.TitleId, level, now));
        }
        else
        {
            rating.Change(level, now);
        }

        metrics.Rated(level.ToString());
        return Result.Success();
    }
}

/// <summary>A title the profile has not rated changes nothing; the title need not be watchable any more.</summary>
internal sealed class RemoveRatingHandler(ProfileAccess profileAccess, ITitleRatingRepository ratings, StreamingMetrics metrics) : ICommandHandler<RemoveRatingCommand>
{
    public async Task<Result> Handle(RemoveRatingCommand command, CancellationToken cancellationToken)
    {
        var found = await profileAccess.FindAsync(command.UserId, command.ProfileId, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        if (await ratings.FindAsync(command.ProfileId, command.TitleId, cancellationToken).ConfigureAwait(false) is { } rating)
        {
            ratings.Remove(rating);
            metrics.Rated(RatingOutcomes.Removed);
        }

        return Result.Success();
    }
}

/// <summary>The <c>rating</c> tag of a rating taken back; the others are the level's name.</summary>
public static class RatingOutcomes
{
    public const string Removed = "removed";
}
