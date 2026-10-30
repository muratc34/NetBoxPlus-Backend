namespace Auth.Domain.Users;

/// <summary>Loads and stores users for commands. Queries read through the application's read model instead.</summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> GetByEmailAsync(EmailAddress email, CancellationToken cancellationToken);

    Task<bool> ExistsByEmailAsync(EmailAddress email, CancellationToken cancellationToken);

    void Add(User user);

    /// <summary>
    /// Counts a failed sign-in and, at <see cref="User.MaxFailedSignIns"/>, locks the account for
    /// <see cref="User.LockoutDuration"/> and starts the count again. Runs in the database at once
    /// and atomically: it must be kept even though the sign-in itself fails (a failed command saves
    /// nothing), and concurrent attempts must each be counted.
    /// </summary>
    Task RecordFailedSignInAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}
