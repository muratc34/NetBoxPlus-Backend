using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;
using Streaming.Application.Access;
using Streaming.Application.Lists;
using Streaming.Domain.Access;
using Streaming.Domain.Lists;
using Streaming.Infrastructure.Persistence;
using Streaming.IntegrationTests.Admin;

namespace Streaming.IntegrationTests.Lists;

/// <summary>A profile's list through the API, against the copies playback reads.</summary>
[Collection(nameof(StreamingApiCollection))]
public sealed class MyListApiTests(StreamingApiFixture api)
{
    private static readonly JsonSerializerOptions Json = VideoApiTests.CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Titles_AreSaved_NewestFirst_OnceEach_AndTakenOff()
    {
        var (user, profile) = await ViewerAsync();
        var (first, second) = (await TitleAsync(), await TitleAsync(TitleKind.Series));
        using var client = api.CreateClientFor(user);

        Assert.Equal(HttpStatusCode.NoContent, (await AddAsync(client, profile, first)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await AddAsync(client, profile, second)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await AddAsync(client, profile, first)).StatusCode);

        var list = await ListAsync(client, profile);
        Assert.Equal([(second, TitleKind.Series), (first, TitleKind.Movie)], list.Select(item => (item.TitleId, item.Kind)));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/profiles/{profile}/my-list/{second}", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/profiles/{profile}/my-list/{second}", Cancellation)).StatusCode);
        Assert.Equal([first], (await ListAsync(client, profile)).Select(item => item.TitleId));
    }

    [Fact]
    public async Task OnlyATitleTheProfileCanWatch_CanBeSaved()
    {
        var (user, profile) = await ViewerAsync(AgeRating.Age13);
        var adult = await TitleAsync(rating: AgeRating.Age18);
        var unpublished = await TitleAsync(playable: false);
        using var client = api.CreateClientFor(user);

        Assert.Equal("Playback.AgeRestricted", await CodeAsync(await AddAsync(client, profile, adult)));
        Assert.Equal("Playback.NotAvailable", await CodeAsync(await AddAsync(client, profile, unpublished)));
        Assert.Equal("Playback.NotAvailable", await CodeAsync(await AddAsync(client, profile, Guid.CreateVersion7())));
        Assert.Empty(await ListAsync(client, profile));
    }

    [Fact]
    public async Task AnotherUsersProfile_OrARemovedOne_IsNotFound()
    {
        var (user, profile) = await ViewerAsync();
        var (stranger, _) = await ViewerAsync();
        var title = await TitleAsync();
        using var owner = api.CreateClientFor(user);
        using var other = api.CreateClientFor(stranger);
        await AddAsync(owner, profile, title);

        Assert.Equal("Playback.ProfileNotFound", await CodeAsync(await AddAsync(other, profile, title)));
        Assert.Equal("Playback.ProfileNotFound", await CodeAsync(await other.GetAsync($"/profiles/{profile}/my-list", Cancellation)));
        Assert.Equal("Playback.ProfileNotFound", await CodeAsync(await other.DeleteAsync($"/profiles/{profile}/my-list/{title}", Cancellation)));
        Assert.Single(await ListAsync(owner, profile));

        await SendAsync(new ApplyProfileCommand(profile, user, null, 2));

        Assert.Equal("Playback.ProfileNotFound", await CodeAsync(await owner.GetAsync($"/profiles/{profile}/my-list", Cancellation)));
        Assert.False(await ListExistsAsync(profile));
    }

    [Fact]
    public async Task ATitleThatCannotBeWatchedNow_IsHidden_AndComesBack()
    {
        var (user, profile) = await ViewerAsync();
        var (title, kept) = (await TitleAsync(), await TitleAsync());
        using var client = api.CreateClientFor(user);
        await AddAsync(client, profile, title);
        await AddAsync(client, profile, kept);

        await SendAsync(new ApplyPlayableCommand(title, TitleKind.Movie, AgeRating.Age13, [], 2));
        Assert.Equal([kept], (await ListAsync(client, profile)).Select(item => item.TitleId));

        await SendAsync(new ApplyPlayableCommand(title, TitleKind.Movie, AgeRating.Age18, [new PlayableItem(title, Guid.CreateVersion7())], 3));
        await SendAsync(new ApplyProfileCommand(profile, user, AgeRating.Age16, 2));
        Assert.Equal([kept], (await ListAsync(client, profile)).Select(item => item.TitleId));

        await SendAsync(new ApplyProfileCommand(profile, user, AgeRating.Age18, 3));
        Assert.Equal([kept, title], (await ListAsync(client, profile)).Select(item => item.TitleId));
    }

    [Fact]
    public async Task AFullList_TakesNoMore()
    {
        var (user, profile) = await ViewerAsync();
        var title = await TitleAsync();
        await using (var scope = api.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
            var list = MyList.Create(profile, user);
            for (var i = 0; i < MyList.MaxEntries; i++)
            {
                Assert.True(list.Add(Guid.CreateVersion7(), DateTimeOffset.UtcNow).IsSuccess);
            }

            context.MyLists.Add(list);
            await context.SaveChangesAsync(Cancellation);
        }

        using var client = api.CreateClientFor(user);
        var response = await AddAsync(client, profile, title);

        Assert.Equal((HttpStatusCode.Conflict, "MyList.Full"), (response.StatusCode, await CodeAsync(response)));
    }

    [Fact]
    public async Task ADeletedAccount_LosesItsLists()
    {
        var (user, profile) = await ViewerAsync();
        var (other, othersProfile) = await ViewerAsync();
        var title = await TitleAsync();
        using (var client = api.CreateClientFor(user))
        {
            await AddAsync(client, profile, title);
        }

        using (var client = api.CreateClientFor(other))
        {
            await AddAsync(client, othersProfile, title);
        }

        await SendAsync(new ForgetUserCommand(user));

        Assert.False(await ListExistsAsync(profile));
        Assert.True(await ListExistsAsync(othersProfile));
    }

    private async Task<(Guid User, Guid Profile)> ViewerAsync(AgeRating rating = AgeRating.Age18)
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var (user, profile) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await SendAsync(new ApplyProfileCommand(profile, user, rating, 1));
        return (user, profile);
    }

    private async Task<Guid> TitleAsync(TitleKind kind = TitleKind.Movie, AgeRating rating = AgeRating.Age13, bool playable = true)
    {
        var title = Guid.CreateVersion7();
        IReadOnlyList<PlayableItem> items = playable ? [new PlayableItem(Guid.CreateVersion7(), Guid.CreateVersion7())] : [];
        await SendAsync(new ApplyPlayableCommand(title, kind, rating, items, 1));
        return title;
    }

    private static Task<HttpResponseMessage> AddAsync(HttpClient client, Guid profile, Guid title)
        => client.PutAsync($"/profiles/{profile}/my-list/{title}", null, Cancellation);

    private static async Task<List<MyListItem>> ListAsync(HttpClient client, Guid profile)
    {
        var response = await client.GetAsync($"/profiles/{profile}/my-list", Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<MyListItem>>(Json, Cancellation))!;
    }

    private async Task<bool> ListExistsAsync(Guid profile)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().MyLists.AnyAsync(list => list.Id == profile, Cancellation);
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
