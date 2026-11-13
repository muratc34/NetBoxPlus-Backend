using Auth.Contracts;
using Billing.Contracts;
using Catalog.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Application.Access;
using Streaming.Domain.Access;
using Streaming.IntegrationTests.Admin;

namespace Streaming.IntegrationTests.Access;

/// <summary>Other services' events, through the inbox as the messaging layer delivers them, into the copies playback reads.</summary>
[Collection(nameof(StreamingApiCollection))]
public sealed class AccessEventTests(StreamingApiFixture api)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    [Fact]
    public async Task ASubscription_FollowsItsNewestRevision_AndALateMessageChangesNothing()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var (subscription, user) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await DeliverAsync(new SubscriptionActivated(subscription, user, Guid.CreateVersion7(), "Hd", 2, Now.AddDays(30), 1, Now));
        Assert.Equal((SubscriptionStatus.Active, VideoQuality.Hd, 2), await SubscriptionAsync(subscription));

        await DeliverAsync(new SubscriptionSuspended(subscription, user, 2, Now));
        await DeliverAsync(new SubscriptionActivated(subscription, user, Guid.CreateVersion7(), "Uhd", 4, Now.AddDays(30), 1, Now));
        Assert.Equal((SubscriptionStatus.Suspended, VideoQuality.Hd, 2), await SubscriptionAsync(subscription));

        await DeliverAsync(new SubscriptionActivated(subscription, user, Guid.CreateVersion7(), "Uhd", 4, Now.AddDays(30), 3, Now));
        Assert.Equal((SubscriptionStatus.Active, VideoQuality.Uhd, 4), await SubscriptionAsync(subscription));

        await DeliverAsync(new SubscriptionCanceled(subscription, user, Now, 4, Now));
        Assert.Equal(SubscriptionStatus.Canceled, (await SubscriptionAsync(subscription)).Status);
    }

    [Fact]
    public async Task AnEndedSubscription_CannotTouchTheUsersNewOne()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var (old, current, user) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await DeliverAsync(new SubscriptionActivated(current, user, Guid.CreateVersion7(), "Sd", 1, null, 1, Now));

        await DeliverAsync(new SubscriptionCanceled(old, user, Now, 5, Now));

        await using var scope = api.CreateScope();
        var copies = await scope.ServiceProvider.GetRequiredService<IStreamingReadModel>().Subscriptions
            .Where(subscription => subscription.UserId == user)
            .ToDictionaryAsync(subscription => subscription.Id, subscription => subscription.Status, Cancellation);
        Assert.Equal(SubscriptionStatus.Active, copies[current]);
        Assert.Equal(SubscriptionStatus.Canceled, copies[old]);
    }

    [Fact]
    public async Task ARemovedProfile_StaysRemoved_WhenAnOlderChangeArrivesLate()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var (profile, user) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await DeliverAsync(new ProfileCreated(user, profile, "All", 1, Now));
        await DeliverAsync(new ProfileRemoved(user, profile, 3, Now));
        await DeliverAsync(new ProfileChanged(user, profile, "Age18", 2, Now));

        var copy = await ProfileAsync(profile);
        Assert.Equal((user, true, AgeRating.All, 3), (copy.UserId, copy.IsRemoved, copy.MaxAgeRating, copy.Revision));
    }

    [Fact]
    public async Task ATitlesList_IsReplacedByANewerOne()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var series = Guid.CreateVersion7();
        var first = new Catalog.Contracts.PlayableItem(Guid.CreateVersion7(), Guid.CreateVersion7(), 100_001);
        var second = new Catalog.Contracts.PlayableItem(Guid.CreateVersion7(), Guid.CreateVersion7(), 100_002);
        var replacement = first with { VideoId = Guid.CreateVersion7() };

        await DeliverAsync(new PlayableChanged(series, "Series", "Age13", [first, second], 1, Now));
        await DeliverAsync(new PlayableChanged(series, "Series", "Age16", [replacement], 2, Now));
        await DeliverAsync(new PlayableChanged(series, "Series", "All", [first, second], 1, Now));

        var title = await TitleAsync(series);
        Assert.Equal((TitleKind.Series, AgeRating.Age16, 2), (title.Kind, title.AgeRating, title.Revision));
        Assert.Equal([new Domain.Access.PlayableItem(replacement.ContentId, replacement.VideoId, 100_001)], title.Items);

        await DeliverAsync(new PlayableChanged(series, "Series", "Age16", [], 3, Now));
        Assert.Empty((await TitleAsync(series)).Items);
    }

    [Fact]
    public async Task UnknownValues_AreRejected_AndKeepNothing()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var (subscription, profile, title) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.Equal("Access.UnknownValue", (await HandleAsync(new SubscriptionActivated(subscription, Guid.CreateVersion7(), Guid.CreateVersion7(), "8k", 1, null, 1, Now))).Error.Code);
        Assert.Equal("Access.UnknownValue", (await HandleAsync(new ProfileCreated(Guid.CreateVersion7(), profile, "21", 1, Now))).Error.Code);
        Assert.Equal("Access.UnknownValue", (await HandleAsync(new PlayableChanged(title, "Episode", "All", [], 1, Now))).Error.Code);

        await using var scope = api.CreateScope();
        var readModel = scope.ServiceProvider.GetRequiredService<IStreamingReadModel>();
        Assert.False(await readModel.Subscriptions.AnyAsync(item => item.Id == subscription, Cancellation));
        Assert.False(await readModel.Profiles.AnyAsync(item => item.Id == profile, Cancellation));
        Assert.False(await readModel.PlayableTitles.AnyAsync(item => item.Id == title, Cancellation));
    }

    [Fact]
    public async Task TwoMessagesAtOnce_OneLosesTheRace_AndItsRetryKeepsTheNewest()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var (profile, user) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        // The later message looks first and finds no copy; the earlier one inserts its copy meanwhile.
        await using (var late = api.CreateScope())
        {
            var profiles = late.ServiceProvider.GetRequiredService<IProfileCopyRepository>();
            Assert.Null(await profiles.GetByIdAsync(profile, Cancellation));
            Assert.True((await SendAsync(new ApplyProfileCommand(profile, user, AgeRating.Age7, 1))).IsSuccess);

            var copy = ProfileCopy.Create(profile, user);
            copy.Apply(AgeRating.Age13, 2);
            profiles.Add(copy);
            await Assert.ThrowsAsync<DataConflictException>(() => late.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Cancellation));
        }

        // The messaging layer retries the loser; it now compares with the winner.
        Assert.True((await SendAsync(new ApplyProfileCommand(profile, user, AgeRating.Age13, 2))).IsSuccess);
        Assert.True((await SendAsync(new ApplyProfileCommand(profile, user, AgeRating.Age7, 1))).IsSuccess);
        Assert.Equal((AgeRating.Age13, 2), ((await ProfileAsync(profile)).MaxAgeRating, (await ProfileAsync(profile)).Revision));
    }

    [Fact]
    public async Task TwoChangesAtOnce_ToAnExistingCopy_CannotBothWin()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var (profile, user) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        Assert.True((await SendAsync(new ApplyProfileCommand(profile, user, AgeRating.All, 1))).IsSuccess);

        await using var firstScope = api.CreateScope();
        await using var secondScope = api.CreateScope();
        var firstCopy = await firstScope.ServiceProvider.GetRequiredService<IProfileCopyRepository>().GetByIdAsync(profile, Cancellation);
        var secondCopy = await secondScope.ServiceProvider.GetRequiredService<IProfileCopyRepository>().GetByIdAsync(profile, Cancellation);
        firstCopy!.Apply(AgeRating.Age16, 3);
        secondCopy!.Apply(AgeRating.Age7, 2);

        await firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Cancellation);
        await Assert.ThrowsAsync<DataConflictException>(() => secondScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Cancellation));

        Assert.Equal(AgeRating.Age16, (await ProfileAsync(profile)).MaxAgeRating);
    }

    [Fact]
    public async Task EveryEvent_HasOneHandler_UnderItsInboxKey()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        await using var scope = api.CreateScope();
        string HandlerOf<TEvent>()
            where TEvent : class
            => Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>()).GetType().FullName!;

        Assert.All([HandlerOf<SubscriptionActivated>(), HandlerOf<SubscriptionSuspended>(), HandlerOf<SubscriptionCanceled>()], name => Assert.Equal("Streaming.Application.Access.FollowSubscriptions", name));
        Assert.All([HandlerOf<ProfileCreated>(), HandlerOf<ProfileChanged>(), HandlerOf<ProfileRemoved>()], name => Assert.Equal("Streaming.Application.Access.FollowProfiles", name));
        Assert.Equal("Streaming.Application.Access.FollowPlayables", HandlerOf<PlayableChanged>());
    }

    /// <summary>Delivers once under a new message id, as the messaging layer does, and expects it handled.</summary>
    private async Task DeliverAsync<TEvent>(TEvent integrationEvent)
        where TEvent : class
    {
        await using var scope = api.CreateScope();
        var handler = Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>());
        Result? result = null;
        var processed = await scope.ServiceProvider.GetRequiredService<IInbox>().ProcessOnceAsync(
            Guid.NewGuid(),
            handler.GetType().FullName!,
            async token => result = await handler.Handle(integrationEvent, token),
            Cancellation);
        Assert.True(processed);
        Assert.True(result!.IsSuccess, result.IsFailure ? result.Error.Code : null);
    }

    private async Task<Result> HandleAsync<TEvent>(TEvent integrationEvent)
        where TEvent : class
    {
        await using var scope = api.CreateScope();
        return await Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>()).Handle(integrationEvent, Cancellation);
    }

    private async Task<Result> SendAsync(ICommand command)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(command, Cancellation);
    }

    private async Task<(SubscriptionStatus Status, VideoQuality Quality, int MaxScreens)> SubscriptionAsync(Guid id)
    {
        await using var scope = api.CreateScope();
        var copy = await scope.ServiceProvider.GetRequiredService<IStreamingReadModel>().Subscriptions.SingleAsync(item => item.Id == id, Cancellation);
        return (copy.Status, copy.Quality, copy.MaxScreens);
    }

    private async Task<ProfileCopy> ProfileAsync(Guid id)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStreamingReadModel>().Profiles.SingleAsync(item => item.Id == id, Cancellation);
    }

    private async Task<PlayableTitle> TitleAsync(Guid id)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStreamingReadModel>().PlayableTitles.SingleAsync(item => item.Id == id, Cancellation);
    }
}
