using System.Net;
using System.Net.Http.Json;
using Notification.Application.SignIns;
using Notification.IntegrationTests.Admin;

namespace Notification.IntegrationTests.SignIns;

[Collection(nameof(NotificationApiCollection))]
public sealed class PreferenceApiTests(NotificationApiFixture api)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ThePreference_IsTheCallersOwn_OnUntilTurnedOff()
    {
        using var client = api.CreateClient();
        using var other = api.CreateClient();

        Assert.True((await client.GetFromJsonAsync<NotificationPreferencesView>("/me/notification-preferences", Cancellation))!.SignInAlerts);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/me/notification-preferences", new { signInAlerts = false }, Cancellation)).StatusCode);

        Assert.False((await client.GetFromJsonAsync<NotificationPreferencesView>("/me/notification-preferences", Cancellation))!.SignInAlerts);
        Assert.True((await other.GetFromJsonAsync<NotificationPreferencesView>("/me/notification-preferences", Cancellation))!.SignInAlerts);

        using var anonymous = api.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/me/notification-preferences", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Unsubscribing_IsAnonymous_LikeAMailProgramsOneClickPost()
    {
        using var anonymous = api.CreateAnonymousClient();
        using var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("List-Unsubscribe", "One-Click")]);

        var response = await anonymous.PostAsync("/unsubscribe?token=not-a-token", form, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Unsubscribe.TokenInvalid", await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
    }
}
