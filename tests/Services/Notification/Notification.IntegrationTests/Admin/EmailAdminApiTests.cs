using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Application.Emails.GetEmails;
using Notification.Domain.Emails;
using Notification.Infrastructure.Emails;
using Notification.Infrastructure.Persistence;
using Shared.Application.Paging;

namespace Notification.IntegrationTests.Admin;

[Collection(nameof(NotificationApiCollection))]
public sealed class EmailAdminApiTests(NotificationApiFixture api)
{
    private const string Read = "notification:emails:read";
    private const string Write = "notification:emails:write";

    private static readonly JsonSerializerOptions Json = CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("GET", "/admin/emails", Read)]
    [InlineData("GET", "/admin/emails/01a1bbcf-1a47-7189-a777-c599de32e2de", Read)]
    [InlineData("POST", "/admin/emails/01a1bbcf-1a47-7189-a777-c599de32e2de/retry", Write)]
    public async Task EveryEndpoint_NeedsItsPermission(string method, string path, string permission)
    {
        using var anonymous = api.CreateAnonymousClient();
        using var other = api.CreateClient(permission == Read ? Write : Read);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Cancellation)).StatusCode);
    }

    [Fact]
    public async Task TheList_IsFiltered_AndShowsNoAddress()
    {
        var user = Guid.CreateVersion7();
        var failed = await QueueAsync(user, rejected: true);
        await QueueAsync(user, rejected: false);
        using var admin = api.CreateClient(Read);

        var page = await ReadAsync<Page<EmailSummary>>(await admin.GetAsync($"/admin/emails?userId={user}&status=Failed", Cancellation));
        var raw = await (await admin.GetAsync($"/admin/emails?userId={user}", Cancellation)).Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(failed, Assert.Single(page.Items).Id);
        Assert.Equal(2, JsonDocument.Parse(raw).RootElement.GetProperty("totalCount").GetInt32());
        Assert.DoesNotContain("@", raw, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/admin/emails?pageSize=101", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task AFailedEmail_IsSentAgain_FromTheFirstAttempt_AndAudited()
    {
        var id = await QueueAsync(Guid.CreateVersion7(), rejected: true);
        using var admin = api.CreateClient(Read, Write);

        var details = await ReadAsync<EmailDetails>(await admin.GetAsync($"/admin/emails/{id}", Cancellation));
        Assert.Equal((EmailStatus.Failed, true), (details.Status, details.HasContent));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/admin/emails/{id}/retry", null, Cancellation)).StatusCode);
        var again = await ReadAsync<EmailDetails>(await admin.GetAsync($"/admin/emails/{id}", Cancellation));
        Assert.Equal((EmailStatus.Pending, 0), (again.Status, again.Attempts));
        Assert.Equal("Email.NotFailed", await CodeAsync(await admin.PostAsync($"/admin/emails/{id}/retry", null, Cancellation)));
        Assert.Equal("Email.NotFound", await CodeAsync(await admin.PostAsync($"/admin/emails/{Guid.CreateVersion7()}/retry", null, Cancellation)));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/admin/emails/{Guid.CreateVersion7()}", Cancellation)).StatusCode);

        await using var scope = api.CreateScope();
        Assert.Equal(["RetryEmail"], await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Set<Shared.Infrastructure.Persistence.Auditing.AuditLogEntry>()
            .Where(entry => entry.TargetId == id).Select(entry => entry.Action).ToListAsync(Cancellation));
    }

    [Fact]
    public async Task AFailedEmailWithoutItsContent_CannotBeSentAgain()
    {
        var id = await QueueAsync(Guid.CreateVersion7(), rejected: true);
        await using (var scope = api.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database
                .ExecuteSqlInterpolatedAsync($"UPDATE \"OutgoingEmails\" SET \"ModifiedAt\" = {DateTimeOffset.UtcNow.AddDays(-8)} WHERE \"Id\" = {id}", Cancellation);
            await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().ForgetOldFailuresAsync(Cancellation);
        }

        using var admin = api.CreateClient(Read, Write);
        Assert.False((await ReadAsync<EmailDetails>(await admin.GetAsync($"/admin/emails/{id}", Cancellation))).HasContent);
        Assert.Equal("Email.ContentGone", await CodeAsync(await admin.PostAsync($"/admin/emails/{id}/retry", null, Cancellation)));
    }

    private async Task<Guid> QueueAsync(Guid user, bool rejected)
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var email = OutgoingEmail.Queue(user, EmailKind.Welcome, Guid.NewGuid().ToString("N"), new EmailContent("Konu", "<p>x</p>", "x"), DateTimeOffset.UtcNow).Value;
        if (rejected)
        {
            email.MarkRejected("SMTP 550");
        }

        context.Emails.Add(email);
        await context.SaveChangesAsync(Cancellation);
        return email.Id;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancellation)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Cancellation))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Cancellation);
        return problem?.Extensions.TryGetValue("code", out var code) == true ? code?.ToString() : $"{(int)response.StatusCode}";
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
