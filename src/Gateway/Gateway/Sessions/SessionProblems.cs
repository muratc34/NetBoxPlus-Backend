using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Shared.Infrastructure.API.Errors;

namespace Gateway.Sessions;

/// <summary>A problem the gateway reports itself, in the same RFC 9457 shape as the services' problems.</summary>
internal sealed record GatewayProblem(int StatusCode, string Code, string Detail) : IResult
{
    public static readonly GatewayProblem SessionRequired = new(
        StatusCodes.Status401Unauthorized, "Session.Required", "Sign in first.");

    public static readonly GatewayProblem SessionExpired = new(
        StatusCodes.Status401Unauthorized, "Session.Expired", "The session has ended. Sign in again.");

    public static readonly GatewayProblem CsrfInvalid = new(
        StatusCodes.Status403Forbidden, "Csrf.Invalid", "The X-CSRF-Token header is missing or does not match the session.");

    public static readonly GatewayProblem OriginNotAllowed = new(
        StatusCodes.Status403Forbidden, "Origin.NotAllowed", "Requests from this origin are not accepted.");

    public static readonly GatewayProblem AuthUnavailable = new(
        StatusCodes.Status503ServiceUnavailable, "Session.AuthUnavailable", "Sign-in is unavailable at the moment. Try again shortly.");

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var typeBaseUri = httpContext.RequestServices.GetRequiredService<IOptions<ErrorResponseOptions>>().Value.TypeBaseUri;
        var problem = new ProblemDetails
        {
            Status = StatusCode,
            Title = ReasonPhrases.GetReasonPhrase(StatusCode),
            Detail = Detail,
            Type = new Uri(typeBaseUri, KebabCase(Code)).AbsoluteUri,
        };
        problem.Extensions["code"] = Code;

        httpContext.Response.StatusCode = StatusCode;
        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        }).ConfigureAwait(false);
    }

    // "Session.AuthUnavailable" → "session-auth-unavailable", as the services name their problem types.
    private static string KebabCase(string code)
    {
        var builder = new StringBuilder(code.Length + 8);
        foreach (var character in code)
        {
            if (character == '.')
            {
                builder.Append('-');
            }
            else if (char.IsUpper(character))
            {
                if (builder.Length > 0 && builder[^1] != '-')
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}

/// <summary>The auth service's own answer, passed through: its problem response, or an empty status.</summary>
internal sealed record AuthProblemResult(AuthResult Result) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        if (Result.IsUnavailable)
        {
            await GatewayProblem.AuthUnavailable.ExecuteAsync(httpContext).ConfigureAwait(false);
            return;
        }

        httpContext.Response.StatusCode = Result.StatusCode;
        if (Result.ProblemBody is { Length: > 0 } body)
        {
            httpContext.Response.ContentType = "application/problem+json";
            await httpContext.Response.WriteAsync(body).ConfigureAwait(false);
        }
    }
}
