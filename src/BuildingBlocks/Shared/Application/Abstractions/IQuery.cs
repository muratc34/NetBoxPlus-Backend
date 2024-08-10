using MediatR;

namespace Shared.Application.Abstractions;

public interface IQuery<out TResponse> : IRequest<TResponse>
{
}
