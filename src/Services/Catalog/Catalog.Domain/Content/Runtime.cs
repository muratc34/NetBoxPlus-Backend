using Shared.Domain;

namespace Catalog.Domain.Content;

/// <summary>The runtime rule shared by movies and episodes.</summary>
public static class Runtime
{
    public const int MaxMinutes = 1000;

    public static Result Check(int minutes)
        => minutes is >= 1 and <= MaxMinutes ? Result.Success() : ContentErrors.RuntimeOutOfRange;
}
