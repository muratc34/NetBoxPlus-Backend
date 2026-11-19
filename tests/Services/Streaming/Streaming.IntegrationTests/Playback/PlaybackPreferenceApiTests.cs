using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;
using Streaming.Application.Access;
using Streaming.Application.Playback.Preferences;
using Streaming.Domain.Access;
using Streaming.Infrastructure.Persistence;
using Streaming.IntegrationTests.Admin;

namespace Streaming.IntegrationTests.Playback;

/// <summary>The languages a profile chose in the player, through the API.</summary>
[Collection(nameof(StreamingApiCollection))]
public sealed class PlaybackPreferenceApiTests(StreamingApiFixture api)
{
    private static readonly JsonSerializerOptions Json = VideoApiTests.CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AChoice_IsKept_AndCanBeUndone()
    {
        var (user, profile) = await ViewerAsync();
        using var client = api.CreateClientFor(user);

        Assert.Equal(new PlaybackPreferences(null, null), await GetAsync(client, profile));

        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(client, profile, "en-US", "tr-TR")).StatusCode);
        Assert.Equal(new PlaybackPreferences("en-US", "tr-TR"), await GetAsync(client, profile));

        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(client, profile, null, null)).StatusCode);
        Assert.Equal(new PlaybackPreferences(null, null), await GetAsync(client, profile));
    }

    [Theory]
    [InlineData("en", null)]
    [InlineData(null, "tr")]
    public async Task OnlyAKnownLanguage_CanBeChosen(string? audio, string? subtitles)
    {
        var (user, profile) = await ViewerAsync();
        using var client = api.CreateClientFor(user);

        var response = await SetAsync(client, profile, audio, subtitles);

        Assert.Equal((HttpStatusCode.BadRequest, "Video.LanguageUnsupported"), (response.StatusCode, await CodeAsync(response)));
        Assert.False(await ExistsAsync(profile));
    }

    [Fact]
    public async Task AnotherUsersProfile_IsNotFound()
    {
        var (_, profile) = await ViewerAsync();
        var (stranger, _) = await ViewerAsync();
        using var client = api.CreateClientFor(stranger);

        Assert.Equal("Playback.ProfileNotFound", await CodeAsync(await SetAsync(client, profile, "en-US", null)));
        Assert.Equal("Playback.ProfileNotFound", await CodeAsync(await client.GetAsync($"/profiles/{profile}/playback-preferences", Cancellation)));
    }

    [Fact]
    public async Task TheChoice_GoesWithTheProfile_AndWithTheAccount()
    {
        var (user, profile) = await ViewerAsync();
        var second = Guid.CreateVersion7();
        await SendAsync(new ApplyProfileCommand(second, user, AgeRating.Age18, 1));
        using (var client = api.CreateClientFor(user))
        {
            await SetAsync(client, profile, "en-US", null);
            await SetAsync(client, second, null, "en-US");
        }

        await SendAsync(new ApplyProfileCommand(profile, user, null, 2));
        Assert.False(await ExistsAsync(profile));
        Assert.True(await ExistsAsync(second));

        await SendAsync(new ForgetUserCommand(user));
        Assert.False(await ExistsAsync(second));
    }

    private async Task<(Guid User, Guid Profile)> ViewerAsync()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var (user, profile) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await SendAsync(new ApplyProfileCommand(profile, user, AgeRating.Age18, 1));
        return (user, profile);
    }

    private static Task<HttpResponseMessage> SetAsync(HttpClient client, Guid profile, string? audioLanguage, string? subtitleLanguage)
        => client.PutAsJsonAsync($"/profiles/{profile}/playback-preferences", new { audioLanguage, subtitleLanguage }, Json, Cancellation);

    private static async Task<PlaybackPreferences> GetAsync(HttpClient client, Guid profile)
        => await VideoApiTests.ReadAsync<PlaybackPreferences>(await client.GetAsync($"/profiles/{profile}/playback-preferences", Cancellation));

    private async Task<bool> ExistsAsync(Guid profile)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().PlaybackPreferences.AnyAsync(item => item.Id == profile, Cancellation);
    }

    private async Task SendAsync(ICommand command)
    {
        await using var scope = api.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(command, Cancellation);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Cancellation);
        return problem?.Extensions.TryGetValue("code", out var code) == true ? code?.ToString() : $"{(int)response.StatusCode}";
    }
}
