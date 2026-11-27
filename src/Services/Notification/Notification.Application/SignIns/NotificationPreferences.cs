using Microsoft.EntityFrameworkCore;
using Notification.Application.Abstractions;
using Notification.Domain.SignIns;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Notification.Application.SignIns;

public sealed record GetNotificationPreferencesQuery(Guid UserId) : IQuery<NotificationPreferencesView>;

/// <param name="SignInAlerts">An e-mail for a sign-in from a new device; on unless turned off.</param>
public sealed record NotificationPreferencesView(bool SignInAlerts);

public sealed record SetNotificationPreferencesCommand(Guid UserId, bool SignInAlerts) : ICommand;

/// <summary>Turns the alerts off with the token of an alert's link (RFC 8058 one-click); no sign-in needed.</summary>
public sealed record UnsubscribeCommand(string? Token) : ICommand;

internal sealed class GetNotificationPreferencesHandler(INotificationReadModel readModel)
    : IQueryHandler<GetNotificationPreferencesQuery, NotificationPreferencesView>
{
    public async Task<Result<NotificationPreferencesView>> Handle(GetNotificationPreferencesQuery query, CancellationToken cancellationToken)
    {
        var signInAlerts = await readModel.Preferences
            .Where(preference => preference.UserId == query.UserId)
            .Select(preference => (bool?)preference.SignInAlerts)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return new NotificationPreferencesView(signInAlerts ?? true);
    }
}

/// <summary>The first change makes the row; two at once race on its key and the loser retries (409 <c>Data.Conflict</c>).</summary>
internal sealed class SetNotificationPreferencesHandler(ISignInRecordRepository records, TimeProvider timeProvider)
    : ICommandHandler<SetNotificationPreferencesCommand>
{
    public async Task<Result> Handle(SetNotificationPreferencesCommand command, CancellationToken cancellationToken)
    {
        await NotificationPreferenceChange.ApplyAsync(records, command.UserId, command.SignInAlerts, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal sealed class UnsubscribeValidator : IValidator<UnsubscribeCommand>
{
    /// <summary>The base64url of <see cref="UnsubscribeLink.TokenBytes"/> bytes, with room to spare.</summary>
    private const int MaxTokenLength = 100;

    public IEnumerable<ValidationFailure> Validate(UnsubscribeCommand instance)
    {
        if (string.IsNullOrEmpty(instance.Token) || instance.Token.Length > MaxTokenLength)
        {
            yield return new ValidationFailure(nameof(instance.Token), SignInAlertErrors.UnsubscribeTokenInvalid.Code, SignInAlertErrors.UnsubscribeTokenInvalid.Message);
        }
    }
}

/// <summary>An unknown and an expired token look alike; a valid one works again and again.</summary>
internal sealed class UnsubscribeHandler(ISignInRecordRepository records, TimeProvider timeProvider) : ICommandHandler<UnsubscribeCommand>
{
    public async Task<Result> Handle(UnsubscribeCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await records.GetLinkAsync(UnsubscribeLink.Hash(command.Token!), cancellationToken).ConfigureAwait(false);
        if (link is null || !link.IsValidAt(now))
        {
            return SignInAlertErrors.UnsubscribeTokenInvalid;
        }

        await NotificationPreferenceChange.ApplyAsync(records, link.UserId, signInAlerts: false, now, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal static class NotificationPreferenceChange
{
    public static async Task ApplyAsync(ISignInRecordRepository records, Guid userId, bool signInAlerts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var preference = await records.GetPreferenceAsync(userId, cancellationToken).ConfigureAwait(false);
        if (preference is null)
        {
            preference = NotificationPreference.Create(userId);
            records.AddPreference(preference);
        }

        preference.Change(signInAlerts, now);
    }
}
