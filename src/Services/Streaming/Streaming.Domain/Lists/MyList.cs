using Shared.Domain;

namespace Streaming.Domain.Lists;

/// <summary>
/// A profile's "my list": titles saved to watch later. The id is the profile's id. Adding or removing
/// changes the version, so two adds at the same moment cannot both pass the limit: the second one to
/// save loses.
/// </summary>
public sealed class MyList : AggregateRoot
{
    /// <summary>The most titles a list keeps.</summary>
    public const int MaxEntries = 200;

    private readonly List<MyListEntry> _entries = [];

    private MyList(Guid profileId, Guid userId)
        : base(profileId)
    {
        UserId = userId;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private MyList()
    {
    }

    /// <summary>Whose profile it is: what an account deletion removes.</summary>
    public Guid UserId { get; private set; }

    public IReadOnlyCollection<MyListEntry> Entries => _entries.AsReadOnly();

    /// <summary>Goes up with every change; the concurrency token.</summary>
    public int Version { get; private set; }

    public static MyList Create(Guid profileId, Guid userId)
    {
        Ensure.NotEmpty(profileId);
        Ensure.NotEmpty(userId);
        return new MyList(profileId, userId);
    }

    /// <summary>Saves the title; one already on the list stays where it is.</summary>
    public Result Add(Guid titleId, DateTimeOffset now)
    {
        Ensure.NotEmpty(titleId);

        if (_entries.Exists(entry => entry.TitleId == titleId))
        {
            return Result.Success();
        }

        if (_entries.Count >= MaxEntries)
        {
            return MyListErrors.Full;
        }

        _entries.Add(new MyListEntry(Id, titleId, now));
        Version++;
        return Result.Success();
    }

    /// <summary>Takes the title off; one that is not on the list changes nothing.</summary>
    public void Remove(Guid titleId)
    {
        if (_entries.RemoveAll(entry => entry.TitleId == titleId) > 0)
        {
            Version++;
        }
    }
}

/// <summary>A title on a profile's list, and when it was put there.</summary>
public sealed class MyListEntry
{
    internal MyListEntry(Guid profileId, Guid titleId, DateTimeOffset addedAt)
    {
        ProfileId = profileId;
        TitleId = titleId;
        AddedAt = addedAt;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private MyListEntry()
    {
    }

    public Guid ProfileId { get; private set; }

    public Guid TitleId { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }
}

public static class MyListErrors
{
    public static readonly Error Full = Error.Conflict("MyList.Full", $"The list already holds {MyList.MaxEntries} titles. Remove one first.");
}

public interface IMyListRepository
{
    /// <summary>The profile's list with its entries, or <see langword="null"/> before its first title.</summary>
    Task<MyList?> GetAsync(Guid profileId, CancellationToken cancellationToken);

    void Add(MyList list);

    /// <summary>Removes, with the change being saved, the list of a profile that no longer exists.</summary>
    Task RemoveForProfileAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Removes, with the change being saved, every list of a user whose account was deleted.</summary>
    Task RemoveForUserAsync(Guid userId, CancellationToken cancellationToken);
}
