using Shared.Application.Abstractions;
using Shared.Domain.Result;

namespace Auth.Application.Users.GetUserById;

public sealed class GetUserByIdQuery : IQuery<Result<GetUserByIdResponse>>
{
    public GetUserByIdQuery(Guid userId) => UserId = userId;

    public Guid UserId { get; }
}
