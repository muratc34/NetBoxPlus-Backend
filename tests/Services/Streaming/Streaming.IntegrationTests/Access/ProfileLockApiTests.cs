using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;
using Streaming.Application.Access;
using Streaming.Domain.Access;
using Streaming.IntegrationTests.Admin;

namespace Streaming.IntegrationTests.Access;

/// <summary>Every endpoint that acts for a profile asks a locked one to be opened in the caller's sign-in.</summary>
[Collection(nameof(StreamingApiCollection))]
public sealed class ProfileLockApiTests(StreamingApiFixture api)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ALockedProfile_IsRefusedEverywhere_UntilItsSignInOpensIt()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var (user, profile, title) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await SendAsync(new ApplyProfileCommand(profile, user, AgeRating.Age18, 1, Locked: true));
        await SendAsync(new ApplyPlayableCommand(title, TitleKind.Movie, AgeRating.All, [new PlayableItem(Guid.CreateVersion7(), Guid.CreateVersion7())], 1));
        using var closed = api.CreateClientFor(user);
        using var opened = api.CreateClientWithUnlocked(user, profile);

        foreach (var request in Requests(profile, title))
        {
            using var refused = await closed.SendAsync(request(), Cancellation);
            Assert.True(refused.StatusCode == HttpStatusCode.Forbidden, $"{refused.RequestMessage!.Method} {refused.RequestMessage.RequestUri}: {(int)refused.StatusCode}");
            Assert.Equal("Playback.ProfileLocked", (await refused.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]!.ToString());

            using var allowed = await opened.SendAsync(request(), Cancellation);
            Assert.True(allowed.IsSuccessStatusCode, $"{allowed.RequestMessage!.Method} {allowed.RequestMessage.RequestUri}: {(int)allowed.StatusCode}");
        }
    }

    private static IEnumerable<Func<HttpRequestMessage>> Requests(Guid profile, Guid title)
    {
        yield return () => new HttpRequestMessage(HttpMethod.Get, $"/profiles/{profile}/my-list");
        yield return () => new HttpRequestMessage(HttpMethod.Put, $"/profiles/{profile}/my-list/{title}");
        yield return () => new HttpRequestMessage(HttpMethod.Delete, $"/profiles/{profile}/my-list/{title}");
        yield return () => new HttpRequestMessage(HttpMethod.Get, $"/profiles/{profile}/progress?titleId={title}");
        yield return () => new HttpRequestMessage(HttpMethod.Get, $"/profiles/{profile}/continue-watching");
        yield return () => new HttpRequestMessage(HttpMethod.Get, $"/profiles/{profile}/playback-preferences");
        yield return () => new HttpRequestMessage(HttpMethod.Put, $"/profiles/{profile}/playback-preferences") { Content = JsonContent.Create(new { audioLanguage = (string?)null, subtitleLanguage = (string?)null }) };
        yield return () => new HttpRequestMessage(HttpMethod.Get, $"/profiles/{profile}/ratings/{title}");
        yield return () => new HttpRequestMessage(HttpMethod.Put, $"/profiles/{profile}/ratings/{title}") { Content = JsonContent.Create(new { rating = "Like" }) };
        yield return () => new HttpRequestMessage(HttpMethod.Delete, $"/profiles/{profile}/ratings/{title}");
        yield return () => new HttpRequestMessage(HttpMethod.Get, $"/profiles/{profile}/history");
        yield return () => new HttpRequestMessage(HttpMethod.Delete, $"/profiles/{profile}/history/{title}");
        yield return () => new HttpRequestMessage(HttpMethod.Get, $"/profiles/{profile}/taste");
    }

    private async Task SendAsync(ICommand command)
    {
        await using var scope = api.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(command, Cancellation)).IsSuccess);
    }
}
