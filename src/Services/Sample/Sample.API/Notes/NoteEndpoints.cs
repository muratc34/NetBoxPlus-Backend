using Microsoft.AspNetCore.Mvc;
using Sample.Application.Notes.CreateNote;
using Sample.Application.Notes.DeleteNote;
using Sample.Application.Notes.GetNote;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;
using Shared.Infrastructure.API.Security;

namespace Sample.API.Notes;

public sealed record CreateNoteRequest(string Title, string Content);

public sealed record CreateNoteResponse(Guid Id);

internal static class SamplePermissions
{
    public const string DeleteNotes = "sample:notes:delete";
}

internal static class NoteEndpoints
{
    public static IEndpointRouteBuilder MapNoteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var notes = endpoints.MapGroup("/notes").WithTags("Notes");

        // Creating and reading are anonymous until the Auth service exists (phase 1), so the walking
        // skeleton can run through the gateway without tokens. Deleting shows a protected endpoint.
        notes.MapPost("/", CreateAsync)
            .WithName("CreateNote")
            .WithSummary("Creates a note.")
            .Produces<CreateNoteResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        notes.MapGet("/{id:guid}", GetAsync)
            .WithName("GetNote")
            .WithSummary("Returns a note.")
            .Produces<NoteResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        notes.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteNote")
            .WithSummary("Deletes a note.")
            .RequirePermission(SamplePermissions.DeleteNotes)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateNoteRequest request,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new CreateNoteCommand(request.Title, request.Content), cancellationToken);

        return result.ToHttpResult(id => TypedResults.Created($"/notes/{id}", new CreateNoteResponse(id)));
    }

    private static async Task<IResult> GetAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetNoteQuery(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> DeleteAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new DeleteNoteCommand(id), cancellationToken)).ToHttpResult();
}
