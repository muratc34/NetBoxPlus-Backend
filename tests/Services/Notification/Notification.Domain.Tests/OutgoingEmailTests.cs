using Notification.Domain.Emails;

namespace Notification.Domain.Tests;

public sealed class OutgoingEmailTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    private static readonly EmailContent Content = new("Hoş geldin", "<p>Merhaba</p>", "Merhaba");

    private static OutgoingEmail Queued() => OutgoingEmail.Queue(Guid.CreateVersion7(), EmailKind.Welcome, "user-1", Content, Now).Value;

    [Fact]
    public void AQueuedEmail_IsDueAtOnce_WithItsContent()
    {
        var email = Queued();

        Assert.Equal((EmailStatus.Pending, 0, Now, "Hoş geldin", "<p>Merhaba</p>", "Merhaba"), (email.Status, email.Attempts, email.NextAttemptAt, email.Subject, email.HtmlBody, email.TextBody));
    }

    [Theory]
    [InlineData("", "Konu")]
    [InlineData("user-1", "")]
    public void AnEmail_NeedsASourceAndASubject(string sourceKey, string subject)
        => Assert.True(OutgoingEmail.Queue(Guid.CreateVersion7(), EmailKind.Welcome, sourceKey, Content with { Subject = subject }, Now).IsFailure);

    [Fact]
    public void AnEmail_NeedsAUser_AndLimitsItsKeyAndSubject()
    {
        Assert.Equal(OutgoingEmailErrors.UserRequired, OutgoingEmail.Queue(Guid.Empty, EmailKind.Welcome, "k", Content, Now).Error);
        Assert.Equal(OutgoingEmailErrors.SourceKeyInvalid, OutgoingEmail.Queue(Guid.CreateVersion7(), EmailKind.Welcome, new string('k', OutgoingEmail.SourceKeyMaxLength + 1), Content, Now).Error);
        Assert.Equal(OutgoingEmailErrors.SubjectInvalid, OutgoingEmail.Queue(Guid.CreateVersion7(), EmailKind.Welcome, "k", Content with { Subject = new string('s', OutgoingEmail.SubjectMaxLength + 1) }, Now).Error);
        Assert.True(OutgoingEmail.Queue(Guid.CreateVersion7(), EmailKind.Welcome, new string('k', OutgoingEmail.SourceKeyMaxLength), Content with { Subject = new string('s', OutgoingEmail.SubjectMaxLength) }, Now).IsSuccess);
    }

    [Fact]
    public void ASentEmail_ForgetsItsContent_ButKeepsItsSubject()
    {
        var email = Queued();

        email.MarkSent(Now.AddMinutes(1));

        Assert.Equal((EmailStatus.Sent, 1, (DateTimeOffset?)Now.AddMinutes(1), "Hoş geldin", (string?)null, (string?)null), (email.Status, email.Attempts, email.SentAt, email.Subject, email.HtmlBody, email.TextBody));
    }

    [Fact]
    public void AnEmailNotSent_WaitsLongerEachTime_ThenIsGivenUp()
    {
        var email = Queued();
        var waits = new List<TimeSpan>();

        for (var attempt = 1; attempt < OutgoingEmail.MaxAttempts; attempt++)
        {
            email.MarkNotSent("SMTP 421", Now);
            waits.Add(email.NextAttemptAt - Now);
            Assert.Equal(EmailStatus.Pending, email.Status);
        }

        email.MarkNotSent("SMTP 421", Now);

        Assert.Equal(
            [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(4), TimeSpan.FromHours(12), TimeSpan.FromHours(12)],
            waits);
        Assert.Equal((EmailStatus.Failed, OutgoingEmail.MaxAttempts, "SMTP 421"), (email.Status, email.Attempts, email.LastError));
        Assert.NotNull(email.HtmlBody);
    }

    [Fact]
    public void ARejectedEmail_FailsAtOnce_AndKeepsItsContentForARetry()
    {
        var email = Queued();

        email.MarkRejected("SMTP 550");

        Assert.Equal((EmailStatus.Failed, 1, "SMTP 550"), (email.Status, email.Attempts, email.LastError));
        Assert.True(email.Retry(Now.AddHours(1)).IsSuccess);
        Assert.Equal((EmailStatus.Pending, 0, Now.AddHours(1)), (email.Status, email.Attempts, email.NextAttemptAt));
    }

    [Fact]
    public void OnlyAFailedEmail_WithItsContent_IsSentAgain()
    {
        var pending = Queued();
        Assert.Equal(OutgoingEmailErrors.NotFailed, pending.Retry(Now).Error);

        var sent = Queued();
        sent.MarkSent(Now);
        Assert.Equal(OutgoingEmailErrors.NotFailed, sent.Retry(Now).Error);

        var forgotten = Queued();
        forgotten.MarkRejected("SMTP 550");
        forgotten.Forget();
        Assert.Equal(OutgoingEmailErrors.ContentGone, forgotten.Retry(Now).Error);
    }

    [Fact]
    public void ALongReason_IsCut()
    {
        var email = Queued();

        email.MarkRejected(new string('x', OutgoingEmail.LastErrorMaxLength + 50));

        Assert.Equal(OutgoingEmail.LastErrorMaxLength, email.LastError!.Length);
    }
}
