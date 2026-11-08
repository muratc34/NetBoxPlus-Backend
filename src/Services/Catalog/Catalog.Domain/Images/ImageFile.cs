using Shared.Domain;

namespace Catalog.Domain.Images;

public enum ImageFileStatus
{
    Pending,
    Active,
    Retired,
}

/// <summary>
/// One image, from the moment an upload is promised to the moment its file is removed. It lets the
/// service recognise its own uploads, use each once, and clean up what nothing refers to.
/// </summary>
public sealed class ImageFile : AggregateRoot, IAuditableEntity
{
    /// <summary>Time to finish an upload.</summary>
    public static readonly TimeSpan UploadLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How long a replaced image stays, so that pages already open keep working.</summary>
    public static readonly TimeSpan RetiredGracePeriod = TimeSpan.FromDays(1);

    private ImageFile(Guid id, DateTimeOffset expiresAt)
        : base(id)
    {
        ExpiresAt = expiresAt;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private ImageFile()
    {
    }

    /// <summary>Set once the file has been checked and stored.</summary>
    public string? Name { get; private set; }

    public ImageFileStatus Status { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RetiredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public static ImageFile StartUpload(DateTimeOffset now) => new(Guid.CreateVersion7(), now + UploadLifetime);

    /// <summary>Whether the upload can still become an image.</summary>
    public Result CheckUsable(DateTimeOffset now)
    {
        if (Status != ImageFileStatus.Pending)
        {
            return ImageErrors.UploadAlreadyUsed;
        }

        return now >= ExpiresAt ? ImageErrors.UploadExpired : Result.Success();
    }

    public void Activate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Status = ImageFileStatus.Active;
    }

    public void Retire(DateTimeOffset now)
    {
        Status = ImageFileStatus.Retired;
        RetiredAt = now;
    }
}
