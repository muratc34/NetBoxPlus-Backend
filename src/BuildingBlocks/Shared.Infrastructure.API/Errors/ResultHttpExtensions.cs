using Microsoft.AspNetCore.Http;
using Shared.Domain;

namespace Shared.Infrastructure.API.Errors;

/// <summary>Turns handler results into HTTP responses in Minimal API endpoints.</summary>
public static class ResultHttpExtensions
{
    /// <summary>Success → <c>204 No Content</c>; failure → RFC 9457 problem details.</summary>
    public static IResult ToHttpResult(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    /// <summary>Success → <c>200 OK</c> with the value; failure → RFC 9457 problem details.</summary>
    public static IResult ToHttpResult<TValue>(this Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <summary>
    /// Success → the response built by <paramref name="onSuccess"/> (for example
    /// <c>TypedResults.Created</c>); failure → RFC 9457 problem details.
    /// </summary>
    public static IResult ToHttpResult<TValue>(this Result<TValue> result, Func<TValue, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess ? onSuccess(result.Value) : result.Error.ToProblem();
    }

    /// <summary>An error as RFC 9457 problem details.</summary>
    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error == Error.None)
        {
            throw new ArgumentException("Error.None is not an error and has no HTTP representation.", nameof(error));
        }

        return new ErrorHttpResult(error);
    }
}
