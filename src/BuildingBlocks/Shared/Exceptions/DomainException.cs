using Shared.Domain.Result;

namespace Shared.Exceptions;

[Serializable]
public class DomainException : Exception
{
    public DomainException(Error error)
            : base(error.Message)
            => Error = error;
    public Error Error { get; }
}
