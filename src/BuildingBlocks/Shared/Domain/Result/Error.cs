namespace Shared.Domain.Result;

public sealed record Error(string code, string message)
{
    public static readonly Error? None = null;
}
