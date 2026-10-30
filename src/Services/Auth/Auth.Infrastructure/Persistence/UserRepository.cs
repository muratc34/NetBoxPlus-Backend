using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Persistence;

internal sealed class UserRepository(AuthDbContext context) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Users.Include(user => user.Roles).FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(EmailAddress email, CancellationToken cancellationToken)
        => context.Users.Include(user => user.Roles).FirstOrDefaultAsync(user => user.Email == email, cancellationToken);

    public Task<bool> ExistsByEmailAsync(EmailAddress email, CancellationToken cancellationToken)
        => context.Users.AnyAsync(user => user.Email == email, cancellationToken);

    public void Add(User user) => context.Users.Add(user);

    public Task RecordFailedSignInAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // One UPDATE: every SET reads the row as it was before it, so the count and the lockout
        // decide from the same value, and concurrent attempts queue on the row lock.
        DateTimeOffset? lockedOutUntil = now + User.LockoutDuration;

        return context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        user => user.LockedOutUntil,
                        user => user.FailedSignInCount + 1 >= User.MaxFailedSignIns ? lockedOutUntil : user.LockedOutUntil)
                    .SetProperty(
                        user => user.FailedSignInCount,
                        user => user.FailedSignInCount + 1 >= User.MaxFailedSignIns ? 0 : user.FailedSignInCount + 1)
                    .SetProperty(user => user.ModifiedAt, now),
                cancellationToken);
    }
}
