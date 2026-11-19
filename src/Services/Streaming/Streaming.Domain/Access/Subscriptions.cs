using Shared.Domain;

namespace Streaming.Domain.Access;

public enum SubscriptionStatus
{
    Active,

    /// <summary>A payment failed; no access until it is paid.</summary>
    Suspended,

    Canceled,
}

/// <summary>The best picture a plan allows.</summary>
public enum VideoQuality
{
    Sd,
    Hd,
    Uhd,
}

/// <summary>
/// The billing service's subscription as far as watching goes, kept from its events. One per
/// subscription, not per user: a late event of an ended subscription cannot touch a newer one.
/// </summary>
public sealed class SubscriptionCopy : AggregateRoot
{
    private SubscriptionCopy(Guid id, Guid userId)
        : base(id)
    {
        UserId = userId;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private SubscriptionCopy()
    {
    }

    public Guid UserId { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    /// <summary>What the plan allowed when the subscription was last active.</summary>
    public VideoQuality Quality { get; private set; }

    public int MaxScreens { get; private set; }

    /// <summary>The billing service's revision of the state kept here; older news is ignored.</summary>
    public int Revision { get; private set; }

    public bool GivesAccess => Status == SubscriptionStatus.Active;

    public static SubscriptionCopy Create(Guid subscriptionId, Guid userId) => new(subscriptionId, userId) { Revision = -1 };

    /// <summary>The subscription gives access, with what its plan allows.</summary>
    public bool Activate(VideoQuality quality, int maxScreens, int revision)
    {
        if (revision <= Revision)
        {
            return false;
        }

        Status = SubscriptionStatus.Active;
        Quality = quality;
        MaxScreens = maxScreens;
        Revision = revision;
        return true;
    }

    /// <summary>The subscription gives no access, for now (suspended) or for good (canceled).</summary>
    public bool Stop(SubscriptionStatus status, int revision)
    {
        if (status == SubscriptionStatus.Active)
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Use Activate to give access.");
        }

        if (revision <= Revision)
        {
            return false;
        }

        Status = status;
        Revision = revision;
        return true;
    }
}

public interface ISubscriptionCopyRepository
{
    Task<SubscriptionCopy?> GetByIdAsync(Guid subscriptionId, CancellationToken cancellationToken);

    void Add(SubscriptionCopy subscription);
}

public static class VideoQualities
{
    /// <summary>The tallest rendition a plan's quality allows: SD up to 480 lines, HD up to 1080, UHD all.</summary>
    public static int MaxHeight(VideoQuality quality) => quality switch
    {
        VideoQuality.Sd => 480,
        VideoQuality.Hd => 1080,
        _ => int.MaxValue,
    };
}
