using Auth.Application.Abstractions;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.Users.GetUsers;

/// <param name="Search">Part of an e-mail address; empty for every user.</param>
public sealed record GetUsersQuery(string? Search, int PageNumber, int PageSize) : IQuery<Page<UserSummary>>;

/// <param name="LockedOutUntil">Set while a lockout after failed sign-ins lasts, and for a while after.</param>
public sealed record UserSummary(
    Guid Id,
    string Email,
    bool IsDisabled,
    DateTimeOffset? LockedOutUntil,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Roles);

internal sealed class GetUsersValidator : IValidator<GetUsersQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetUsersQuery instance)
    {
        if (!PageLimits.IsValid(instance.PageNumber, instance.PageSize))
        {
            yield return new ValidationFailure("Page", PageLimits.OutOfRange.Code, PageLimits.OutOfRange.Message);
        }

        if (instance.Search?.Length > EmailAddress.MaxLength)
        {
            yield return new ValidationFailure(nameof(instance.Search), UserErrors.EmailTooLong.Code, UserErrors.EmailTooLong.Message);
        }
    }
}

/// <summary>Newest accounts first.</summary>
internal sealed class GetUsersHandler(IAuthReadModel readModel) : IQueryHandler<GetUsersQuery, Page<UserSummary>>
{
    public async Task<Result<Page<UserSummary>>> Handle(GetUsersQuery query, CancellationToken cancellationToken)
    {
        var users = string.IsNullOrWhiteSpace(query.Search)
            ? readModel.Users
            : readModel.UsersWithEmailContaining(query.Search);

        var totalCount = await users.CountAsync(cancellationToken).ConfigureAwait(false);

        // Version 7 ids grow with creation time, so they break ties between accounts created together.
        var page = await users
            .Include(user => user.Roles)
            .OrderByDescending(user => user.CreatedAt)
            .ThenByDescending(user => user.Id)
            .Skip(PageLimits.Offset(query.PageNumber, query.PageSize))
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var roleIds = page.SelectMany(user => user.Roles).Select(role => role.RoleId).Distinct().ToList();
        var roleNames = await readModel.Roles
            .Where(role => roleIds.Contains(role.Id))
            .ToDictionaryAsync(role => role.Id, role => role.Name, cancellationToken)
            .ConfigureAwait(false);

        var items = page
            .Select(user => new UserSummary(
                user.Id,
                user.Email.Value,
                user.IsDisabled,
                user.LockedOutUntil,
                user.CreatedAt,
                [.. user.Roles.Select(role => roleNames[role.RoleId]).Order(StringComparer.Ordinal)]))
            .ToList();

        return new Page<UserSummary>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
