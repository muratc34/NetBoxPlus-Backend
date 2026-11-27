namespace Notification.Infrastructure.Email;

/// <summary>The headers of RFC 8058 one-click unsubscribe, the same for every provider.</summary>
internal static class UnsubscribeHeaders
{
    public const string ListUnsubscribe = "List-Unsubscribe";

    public const string ListUnsubscribePost = "List-Unsubscribe-Post";

    public const string OneClick = "List-Unsubscribe=One-Click";

    public static string ListUnsubscribeValue(Uri address) => $"<{address.AbsoluteUri}>";
}
