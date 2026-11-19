using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Auth.API.Profiles;
using Auth.API.Sessions;
using Auth.Application.Profiles.AnnounceProfiles;
using Auth.Application.Profiles.GetProfiles;
using Auth.Application.Sessions;
using Auth.Contracts;
using Auth.Domain.Users;
using Auth.Infrastructure.Persistence;
using Auth.IntegrationTests.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;

namespace Auth.IntegrationTests.Profiles;

[Collection(nameof(AuthApiCollection))]
public sealed class ProfileApiTests(AuthApiFixture api)
{
    private const string Password = "correct horse battery staple";
    private static readonly JsonSerializerOptions Json = CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANewAccount_HasItsFirstProfile_AndItIsPublished()
    {
        var (client, userId) = await SignedInAsync();

        var profile = Assert.Single(await ProfilesAsync(client));

        Assert.Equal(("Main", AgeRating.Age18), (profile.Name, profile.MaxAgeRating));
        var created = Assert.Single(await PublishedAsync<ProfileCreated>(userId));
        Assert.Equal((profile.Id, "Age18", 1), (created.ProfileId, created.MaxAgeRating, created.Revision));
    }

    [Fact]
    public async Task TheAnnouncement_RepeatsEveryProfile_AsItIsNow()
    {
        var (client, userId) = await SignedInAsync();
        var profile = Assert.Single(await ProfilesAsync(client));

        await using (var scope = api.CreateScope())
        {
            var announced = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new AnnounceProfilesCommand(), Cancellation);
            Assert.True(announced.Value >= 1);
        }

        var repeated = Assert.Single(await PublishedAsync<ProfileChanged>(userId));
        Assert.Equal((profile.Id, "Age18", 1), (repeated.ProfileId, repeated.MaxAgeRating, repeated.Revision));
    }

    [Fact]
    public async Task Profiles_AreAdded_Changed_AndRemoved_WithEachStepPublished()
    {
        var (client, userId) = await SignedInAsync();

        var create = await client.PostAsJsonAsync("/profiles", new ProfileRequest("Kids", AgeRating.Age7), Json, Cancellation);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<CreatedProfileResponse>(Json, Cancellation))!.Id;
        Assert.Equal($"/profiles/{id}", create.Headers.Location?.OriginalString);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/profiles/{id}", new ProfileRequest("Children", AgeRating.All), Json, Cancellation)).StatusCode);
        Assert.Contains(await ProfilesAsync(client), profile => profile.Id == id && profile.Name == "Children" && profile.MaxAgeRating == AgeRating.All);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/profiles/{id}", Cancellation)).StatusCode);
        Assert.DoesNotContain(await ProfilesAsync(client), profile => profile.Id == id);

        Assert.Equal(("All", 2), Assert.Single(await PublishedAsync<ProfileChanged>(userId)) is var changed ? (changed.MaxAgeRating, changed.Revision) : default);
        Assert.Equal((id, 3), Assert.Single(await PublishedAsync<ProfileRemoved>(userId)) is var removed ? (removed.ProfileId, removed.Revision) : default);
    }

    [Fact]
    public async Task TheRules_AreReported()
    {
        var (client, _) = await SignedInAsync();
        var main = Assert.Single(await ProfilesAsync(client)).Id;

        await AssertProblemAsync(await client.PostAsJsonAsync("/profiles", new ProfileRequest("MAIN", null), Json, Cancellation), HttpStatusCode.Conflict, "Profile.NameTaken");
        await AssertProblemAsync(await client.PostAsJsonAsync("/profiles", new ProfileRequest(" ", null), Json, Cancellation), HttpStatusCode.BadRequest, "Profile.NameRequired");
        await AssertProblemAsync(await client.DeleteAsync($"/profiles/{main}", Cancellation), HttpStatusCode.Conflict, "Profile.LastProfile");
        await AssertProblemAsync(await client.DeleteAsync($"/profiles/{Guid.NewGuid()}", Cancellation), HttpStatusCode.NotFound, "Profile.NotFound");

        for (var i = 2; i <= User.MaxProfiles; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/profiles", new ProfileRequest($"P{i}", null), Json, Cancellation)).StatusCode);
        }

        await AssertProblemAsync(await client.PostAsJsonAsync("/profiles", new ProfileRequest("Sixth", null), Json, Cancellation), HttpStatusCode.Conflict, "Profile.LimitReached");
    }

    [Fact]
    public async Task OnlyTheirOwnProfiles_AreReachable()
    {
        var (owner, _) = await SignedInAsync();
        var (other, _) = await SignedInAsync();
        var mine = Assert.Single(await ProfilesAsync(owner)).Id;

        Assert.DoesNotContain(await ProfilesAsync(other), profile => profile.Id == mine);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/profiles/{mine}", new ProfileRequest("Mine now", null), Json, Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/profiles/{mine}", Cancellation)).StatusCode);

        using var anonymous = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/profiles", Cancellation)).StatusCode);
    }

    private async Task<(HttpClient Client, Guid UserId)> SignedInAsync()
    {
        var client = api.CreateClient();
        var email = $"viewer-{Guid.NewGuid():N}@example.com";
        var registered = await client.PostAsJsonAsync("/register", new RegisterRequest(email, Password), Cancellation);
        Assert.True(registered.IsSuccessStatusCode, await registered.Content.ReadAsStringAsync(Cancellation));
        var userId = (await registered.Content.ReadFromJsonAsync<RegisterResponse>(Cancellation))!.Id;
        var tokens = await (await client.PostAsJsonAsync("/sign-in", new SignInRequest(email, Password), Cancellation)).Content.ReadFromJsonAsync<TokenPair>(Cancellation);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return (client, userId);
    }

    private static async Task<List<ProfileResponse>> ProfilesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/profiles", Cancellation);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancellation));
        return (await response.Content.ReadFromJsonAsync<List<ProfileResponse>>(Json, Cancellation))!;
    }

    private async Task<List<T>> PublishedAsync<T>(Guid userId)
    {
        await using var scope = api.CreateScope();
        var payloads = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().OutboxMessages
            .Where(message => message.Type == IntegrationEvents.MessageTypeOf(typeof(T)))
            .Select(message => message.Payload)
            .ToListAsync(Cancellation);
        return [.. payloads.Where(payload => payload.Contains(userId.ToString(), StringComparison.Ordinal))
            .Select(payload => JsonSerializer.Deserialize<T>(payload, IntegrationEvents.SerializerOptions)!)];
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync(Cancellation);
        Assert.True(response.StatusCode == status, $"{(int)response.StatusCode}: {body}");
        Assert.Contains($"\"code\":\"{code}\"", body, StringComparison.Ordinal);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
