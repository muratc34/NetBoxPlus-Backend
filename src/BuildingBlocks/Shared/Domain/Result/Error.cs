namespace Shared.Domain.Result;

public sealed record Error(string Code, string Message)
{
    public static readonly Error? None = null;
}
