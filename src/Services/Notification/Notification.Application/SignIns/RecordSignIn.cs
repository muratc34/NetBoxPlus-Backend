using Auth.Contracts;
using Notification.Application.Abstractions;
using Notification.Application.Emails;
using Notification.Domain.Emails;
using Notification.Domain.Recipients;
using Notification.Domain.SignIns;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.Application.SignIns;

/// <summary>A sign-in the account service announced. Sent by the event handler, not by people.</summary>
/// <param name="Admin">Signed in to the admin panel.</param>
/// <param name="Registration">The session registering opened: the device is the account's own, nobody is told.</param>
public sealed record RecordSignInCommand(Guid UserId, Guid SessionId, string? DeviceLabel, bool Admin, DateTimeOffset SignedInAt, bool Registration) : ICommand;

/// <summary>
/// Remembers the device; one not seen for <see cref="KnownDevice.RememberedFor"/> gets an alert, unless the user turned
/// alerts off. One e-mail per sign-in. Two sign-ins at once from the same new device race on the device's key: the loser
/// retries (409 <c>Data.Conflict</c>), finds the device known and sends nothing. A deleted account is told nothing.
/// </summary>
internal sealed class RecordSignInHandler(
    ISignInRecordRepository records,
    IRecipientRepository recipients,
    IOutgoingEmailRepository emails,
    IEmailLinks links,
    TimeProvider timeProvider) : ICommandHandler<RecordSignInCommand>
{
    public async Task<Result> Handle(RecordSignInCommand command, CancellationToken cancellationToken)
    {
        var recipient = await recipients.GetAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (recipient?.DeletedAt is not null)
        {
            return Result.Success();
        }

        var label = KnownDevice.LabelOf(command.DeviceLabel);
        var device = await records.GetDeviceAsync(command.UserId, label, cancellationToken).ConfigureAwait(false);
        var known = device?.IsKnownAt(command.SignedInAt) == true;
        if (device is null)
        {
            records.AddDevice(KnownDevice.Create(command.UserId, label, command.SignedInAt));
        }
        else
        {
            device.Seen(command.SignedInAt);
        }

        if (known || command.Registration)
        {
            return Result.Success();
        }

        var preference = await records.GetPreferenceAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        var sourceKey = command.SessionId.ToString("N");
        if (preference?.SignInAlerts == false || await emails.ExistsAsync(EmailKind.NewSignIn, sourceKey, cancellationToken).ConfigureAwait(false))
        {
            return Result.Success();
        }

        var now = timeProvider.GetUtcNow();
        var (link, token) = UnsubscribeLink.Issue(command.UserId, now);
        records.AddLink(link);

        var escaped = Uri.EscapeDataString(token);
        var content = EmailTemplates.NewSignIn(label, command.Admin, command.SignedInAt, links.To("account/security"), links.To($"unsubscribe?token={escaped}"));
        var email = OutgoingEmail.Queue(command.UserId, EmailKind.NewSignIn, sourceKey, content, now, unsubscribeUrl: links.Api($"unsubscribe?token={escaped}"));
        if (email.IsFailure)
        {
            return email.Error;
        }

        emails.Add(email.Value);
        return Result.Success();
    }
}

/// <summary>
/// Sign-ins, from the account service. The full type name is this handler's inbox key: renaming or moving it would
/// process the messages it already handled again.
/// </summary>
internal sealed class RecordSignInOnSignedIn(IDispatcher dispatcher) : IIntegrationEventHandler<SignedIn>
{
    public Task<Result> Handle(SignedIn integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(
            new RecordSignInCommand(
                integrationEvent.UserId,
                integrationEvent.SessionId,
                integrationEvent.DeviceLabel,
                string.Equals(integrationEvent.Kind, "Admin", StringComparison.Ordinal),
                integrationEvent.SignedInAt,
                integrationEvent.Registration),
            cancellationToken);
}
