using Auth.Application.Profiles.GetProfiles;
using Auth.Application.Profiles.ManageProfiles;
using Auth.Application.Profiles.ProfilePins;
using Auth.API.Sessions;
using Auth.Domain.Users;
using Microsoft.AspNetCore.Mvc;
using Shared.Application;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;

namespace Auth.API.Profiles;

/// <param name="MaxAgeRating">The oldest audience the profile may watch for; adult when left out.</param>
public sealed record ProfileRequest(string? Name, AgeRating? MaxAgeRating);

public sealed record CreatedProfileResponse(Guid Id);

/// <param name="Password">The account's password, asked again.</param>
/// <param name="Pin">Four digits; <see langword="null"/> takes the PIN off.</param>
public sealed record ProfilePinRequest(string Password, string? Pin);

public sealed record UnlockProfileRequest(string Pin);

/// <summary>The caller's own profiles; the only auth endpoints a browser reaches through the gateway.</summary>
internal static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/profiles").WithTags("Profiles").RequireAuthorization();

        group.MapGet("/", GetAsync)
            .WithName("GetProfiles")
            .WithSummary("Lists the caller's profiles.")
            .Produces<IReadOnlyList<ProfileResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/", CreateAsync)
            .WithName("CreateProfile")
            .WithSummary("Adds a profile to the caller's account.")
            .Produces<CreatedProfileResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateProfile")
            .WithSummary("Renames a profile or changes its age rating.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", RemoveAsync)
            .WithName("RemoveProfile")
            .WithSummary("Removes a profile; the last one cannot be removed.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}/pin", ChangePinAsync)
            .WithName("ChangeProfilePin")
            .WithSummary("Puts a PIN on a profile, replaces it or takes it off; asks for the password again.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/unlock", UnlockAsync)
            .WithName("UnlockProfile")
            .WithSummary("Opens a locked profile for this sign-in; the next access token names it in profile_unlocked.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> ChangePinAsync(Guid id, [FromBody] ProfilePinRequest request, [FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new ChangeProfilePinCommand(currentUser.Id!.Value, id, request.Password, request.Pin), cancellationToken)).ToHttpResult();

    private static async Task<IResult> UnlockAsync(Guid id, [FromBody] UnlockProfileRequest request, HttpContext context, [FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        if (SessionEndpoints.SessionOf(context) is not { } sessionId)
        {
            return Shared.Domain.Result.Failure(ProfileErrors.SessionRequired).ToHttpResult();
        }

        return (await dispatcher.Send(new UnlockProfileCommand(currentUser.Id!.Value, sessionId, id, request.Pin), cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> GetAsync([FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetProfilesQuery(currentUser.Id!.Value), cancellationToken)).ToHttpResult();

    private static async Task<IResult> CreateAsync([FromBody] ProfileRequest request, [FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new CreateProfileCommand(currentUser.Id!.Value, request.Name, request.MaxAgeRating ?? AgeRating.Age18), cancellationToken);
        return result.ToHttpResult(id => TypedResults.Created($"/profiles/{id}", new CreatedProfileResponse(id)));
    }

    private static async Task<IResult> UpdateAsync(Guid id, [FromBody] ProfileRequest request, [FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new UpdateProfileCommand(currentUser.Id!.Value, id, request.Name, request.MaxAgeRating ?? AgeRating.Age18), cancellationToken)).ToHttpResult();

    private static async Task<IResult> RemoveAsync(Guid id, [FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new RemoveProfileCommand(currentUser.Id!.Value, id), cancellationToken)).ToHttpResult();
}
