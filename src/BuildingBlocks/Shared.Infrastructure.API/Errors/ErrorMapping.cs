using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Shared.Application.Validation;
using Shared.Domain;

namespace Shared.Infrastructure.API.Errors;

/// <summary>
/// The single place where an <see cref="Error"/> becomes an HTTP response: the status code comes
/// from <see cref="ErrorType"/>, everything else from the error itself.
/// </summary>
internal static class ErrorMapping
{
    public const string CodeExtension = "code";
    public const string ErrorCodesExtension = "errorCodes";

    public static int ToStatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Failure => StatusCodes.Status500InternalServerError,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "A successful result has no HTTP error."),
    };

    public static ProblemDetails ToProblemDetails(Error error, ErrorResponseOptions options)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(options);

        var status = ToStatusCode(error.Type);

        var problem = error is ValidationError validation
            ? new HttpValidationProblemDetails(GroupByProperty(validation, failure => failure.Message))
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = ReasonPhrases.GetReasonPhrase(status);
        problem.Detail = error.Message;
        problem.Type = new Uri(options.TypeBaseUri, ToKebabCase(error.Code)).AbsoluteUri;
        problem.Extensions[CodeExtension] = error.Code;

        if (error is ValidationError failed)
        {
            // Messages are for humans; codes let clients localize and react to individual fields.
            problem.Extensions[ErrorCodesExtension] = GroupByProperty(failed, failure => failure.Code);
        }

        return problem;
    }

    /// <summary><c>User.DuplicateEmail</c> → <c>user-duplicate-email</c>.</summary>
    public static string ToKebabCase(string code)
    {
        var builder = new StringBuilder(code.Length + 8);

        for (var i = 0; i < code.Length; i++)
        {
            var current = code[i];

            if (current is '.' or '_' or '-' or ' ')
            {
                AppendSeparator(builder);
                continue;
            }

            if (char.IsUpper(current) && i > 0 && StartsNewWord(code, i))
            {
                AppendSeparator(builder);
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    private static Dictionary<string, string[]> GroupByProperty(ValidationError error, Func<ValidationFailure, string> select)
        => error.Failures
            .GroupBy(failure => JsonNamingPolicy.CamelCase.ConvertName(failure.PropertyName), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(select).ToArray(), StringComparer.Ordinal);

    // An upper-case letter starts a word after a lower-case letter or digit ("DuplicateEmail"),
    // or at the end of an acronym ("HTTPError" → "http-error").
    private static bool StartsNewWord(string code, int index)
    {
        var previous = code[index - 1];
        var nextIsLower = index + 1 < code.Length && char.IsLower(code[index + 1]);

        return char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower);
    }

    private static void AppendSeparator(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != '-')
        {
            builder.Append('-');
        }
    }
}
