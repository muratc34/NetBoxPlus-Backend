namespace Shared.Application;

/// <summary>The caller of the current request.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The user's id, or <see langword="null"/> when the caller is anonymous.</summary>
    Guid? Id { get; }

    bool HasPermission(string permission);
}
