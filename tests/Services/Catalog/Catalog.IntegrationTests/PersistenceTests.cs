using Catalog.Domain.Content;
using Catalog.Domain.Genres;
using Catalog.Domain.Movies;
using Catalog.Domain.People;
using Catalog.Domain.Shows;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.IntegrationTests;

[Collection(nameof(CatalogDatabaseCollection))]
public sealed class PersistenceTests(CatalogDatabaseFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Movie_IsStoredWithItsDescriptionGenresAndCredits()
    {
        await using var services = await database.CreateServicesAsync();
        var genre = Genre.Create("Dram").Value;
        var person = Person.Create("Ada", null).Value;
        var movie = NewMovie();
        movie.SetGenres([genre.Id]);
        movie.SetCredits([new CreditInput(person.Id, CreditRole.Actor, "Hero", 1)]);
        movie.Publish(Now);
        await SaveAsync(services, context => context.AddRange(genre, person, movie));

        var found = await LoadMovieAsync(services, movie.Id);

        Assert.NotNull(found);
        Assert.Equal(movie.Info, found.Info);
        Assert.Equal(PublicationStatus.Published, found.Status);
        Assert.Equal(Now, found.PublishedAt);
        Assert.Equal(genre.Id, Assert.Single(found.Genres).GenreId);
        var credit = Assert.Single(found.Credits);
        Assert.Equal((person.Id, CreditRole.Actor, "Hero", 1), (credit.PersonId, credit.Role, credit.CharacterName, credit.Order));
        Assert.NotEqual(default, found.CreatedAt);
    }

    [Fact]
    public async Task Credits_AndGenres_CanBeReplacedOnAStoredMovie()
    {
        await using var services = await database.CreateServicesAsync();
        var kept = Genre.Create("Dram").Value;
        var dropped = Genre.Create("Komedi").Value;
        var person = Person.Create("Ada", null).Value;
        var other = Person.Create("Grace", null).Value;
        var movie = NewMovie();
        movie.SetGenres([kept.Id, dropped.Id]);
        movie.SetCredits([new CreditInput(person.Id, CreditRole.Actor, "Old", 1), new CreditInput(other.Id, CreditRole.Actor, null, 2)]);
        await SaveAsync(services, context => context.AddRange(kept, dropped, person, other, movie));

        await using (var scope = services.CreateAsyncScope())
        {
            var tracked = await scope.ServiceProvider.GetRequiredService<IMovieRepository>().GetByIdAsync(movie.Id, Cancellation);
            tracked!.SetGenres([kept.Id]);
            tracked.SetCredits([new CreditInput(person.Id, CreditRole.Actor, "New", 9), new CreditInput(person.Id, CreditRole.Director, null, 10)]);
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync(Cancellation);
        }

        var found = await LoadMovieAsync(services, movie.Id);
        Assert.Equal(kept.Id, Assert.Single(found!.Genres).GenreId);
        Assert.Equal(2, found.Credits.Count);
        Assert.Equal("New", found.Credits.Single(credit => credit.Role == CreditRole.Actor).CharacterName);
    }

    [Fact]
    public async Task Series_LoadsItsSeasonsAndEpisodes_AndTakesNewOnes()
    {
        await using var services = await database.CreateServicesAsync();
        var series = Series.Create(Info());
        var season = series.AddSeason(1, "First").Value;
        var episode = series.AddEpisode(season.Id, Details(1)).Value;
        series.PublishEpisode(episode.Id, Now);
        await SaveAsync(services, context => context.Add(series));

        Guid addedSeasonId;
        await using (var scope = services.CreateAsyncScope())
        {
            var tracked = await scope.ServiceProvider.GetRequiredService<ISeriesRepository>().GetByIdAsync(series.Id, Cancellation);
            series = tracked!;
            Assert.True(tracked!.AddEpisode(season.Id, Details(2)).IsSuccess);
            addedSeasonId = tracked.AddSeason(2, null).Value.Id;
            Assert.True(tracked.AddEpisode(addedSeasonId, Details(1)).IsSuccess);
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync(Cancellation);
        }

        await using (var scope = services.CreateAsyncScope())
        {
            var found = await scope.ServiceProvider.GetRequiredService<ISeriesRepository>().GetByIdAsync(series.Id, Cancellation);
            Assert.Equal(2, found!.Seasons.Count);
            var first = found.Seasons.Single(item => item.Number == 1);
            Assert.Equal(2, first.Episodes.Count);
            Assert.True(first.Episodes.Single(item => item.Number == 1).IsPublished);
            Assert.False(first.Episodes.Single(item => item.Number == 2).IsPublished);
            Assert.Single(found.Seasons.Single(item => item.Id == addedSeasonId).Episodes);
        }
    }

    [Fact]
    public async Task RemovedEpisodeAndSeason_LeaveTheDatabase()
    {
        await using var services = await database.CreateServicesAsync();
        var series = Series.Create(Info());
        var season = series.AddSeason(1, null).Value;
        var episode = series.AddEpisode(season.Id, Details(1)).Value;
        await SaveAsync(services, context => context.Add(series));

        await using (var scope = services.CreateAsyncScope())
        {
            var tracked = await scope.ServiceProvider.GetRequiredService<ISeriesRepository>().GetByIdAsync(series.Id, Cancellation);
            Assert.True(tracked!.RemoveEpisode(episode.Id).IsSuccess);
            Assert.True(tracked.RemoveSeason(season.Id).IsSuccess);
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync(Cancellation);
        }

        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.Equal(0, await context.Set<Episode>().CountAsync(Cancellation));
            Assert.Equal(0, await context.Set<Season>().CountAsync(Cancellation));
        }
    }

    [Fact]
    public async Task DeletedMovie_IsHidden_ButItsRowAndGenresStay()
    {
        await using var services = await database.CreateServicesAsync();
        var genre = Genre.Create("Dram").Value;
        var movie = NewMovie();
        movie.SetGenres([genre.Id]);
        await SaveAsync(services, context => context.AddRange(genre, movie));

        await using (var scope = services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IMovieRepository>();
            repository.Remove((await repository.GetByIdAsync(movie.Id, Cancellation))!);
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync(Cancellation);
        }

        Assert.Null(await LoadMovieAsync(services, movie.Id));

        await using (var scope = services.CreateAsyncScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Movies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(item => item.Id == movie.Id, Cancellation);

            Assert.True(stored.IsDeleted);
            Assert.NotNull(stored.DeletedAt);
            Assert.Single(stored.Genres);
        }
    }

    [Fact]
    public async Task GenreInUse_CannotBeDeleted()
    {
        await using var services = await database.CreateServicesAsync();
        var genre = Genre.Create("Dram").Value;
        var movie = NewMovie();
        movie.SetGenres([genre.Id]);
        await SaveAsync(services, context => context.AddRange(genre, movie));

        await Assert.ThrowsAsync<DbUpdateException>(() => RemoveGenreAsync(services, genre.Id));
    }

    [Fact]
    public async Task PersonInUse_CannotBeDeleted()
    {
        await using var services = await database.CreateServicesAsync();
        var person = Person.Create("Ada", null).Value;
        var series = Series.Create(Info());
        series.SetCredits([new CreditInput(person.Id, CreditRole.Creator, null, 0)]);
        await SaveAsync(services, context => context.AddRange(person, series));

        await using var scope = services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPersonRepository>();
        repository.Remove((await repository.GetByIdAsync(person.Id, Cancellation))!);

        await Assert.ThrowsAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync(Cancellation));
    }

    [Fact]
    public async Task UnusedGenre_CanBeDeleted()
    {
        await using var services = await database.CreateServicesAsync();
        var genre = Genre.Create("Dram").Value;
        await SaveAsync(services, context => context.Add(genre));

        await RemoveGenreAsync(services, genre.Id);

        await using var scope = services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Genres.CountAsync(Cancellation));
    }

    [Fact]
    public async Task GenreNames_AreUniqueByTurkishCase()
    {
        await using var services = await database.CreateServicesAsync();
        await SaveAsync(services, context => context.Add(Genre.Create("ilk").Value));

        await using (var scope = services.CreateAsyncScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IGenreRepository>().ExistsByNameAsync(" İlk ", Cancellation));
            Assert.False(await scope.ServiceProvider.GetRequiredService<IGenreRepository>().ExistsByNameAsync("Ilk", Cancellation));
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(services, context => context.Add(Genre.Create("İLK").Value)));
    }

    [Fact]
    public async Task SeasonAndEpisodeNumbers_AreUniqueInTheDatabase()
    {
        await using var services = await database.CreateServicesAsync();
        var series = Series.Create(Info());
        var season = series.AddSeason(1, null).Value;
        var episode = series.AddEpisode(season.Id, Details(1)).Value;
        var second = series.AddEpisode(season.Id, Details(2)).Value;
        await SaveAsync(services, context => context.Add(series));

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        // Past the domain's check, as two concurrent requests would be.
        var duplicate = () => context.Database.ExecuteSqlAsync($"UPDATE \"Episodes\" SET \"Number\" = 1 WHERE \"Id\" = {second.Id}", Cancellation);

        await Assert.ThrowsAsync<Npgsql.PostgresException>(duplicate);
        Assert.NotEqual(episode.Id, second.Id);
    }

    private static ContentInfo Info() => ContentInfo.Create("Night Train", "Yolculuk", "A long journey.", 2024, AgeRating.Age13).Value;

    private static Movie NewMovie() => Movie.Create(Info(), 120).Value;

    private static EpisodeDetails Details(int number) => EpisodeDetails.Create(number, $"Episode {number}", "Something happens.", 45).Value;

    private static async Task SaveAsync(ServiceProvider services, Action<CatalogDbContext> change)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        change(context);
        await context.SaveChangesAsync(Cancellation);
    }

    private static async Task<Movie?> LoadMovieAsync(ServiceProvider services, Guid id)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMovieRepository>().GetByIdAsync(id, Cancellation);
    }

    private static async Task RemoveGenreAsync(ServiceProvider services, Guid id)
    {
        await using var scope = services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IGenreRepository>();
        repository.Remove((await repository.GetByIdAsync(id, Cancellation))!);
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync(Cancellation);
    }
}
