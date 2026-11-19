using Catalog.Application.People.CreatePerson;
using Catalog.Application.People.DeletePerson;
using Catalog.Application.People.GetPeople;
using Catalog.Application.People.GetPerson;
using Catalog.Application.People.UpdatePerson;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Infrastructure.API.Errors;
using Shared.Infrastructure.API.Security;

namespace Catalog.API.Admin;

/// <summary>People to credit, for the admin panel. Every change is written to the audit log.</summary>
internal static class PersonAdminEndpoints
{
    public static IEndpointRouteBuilder MapPersonAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/people").WithTags("People");

        group.MapGet("/", GetPeopleAsync)
            .WithName("GetPeople")
            .WithSummary("Lists people by name.")
            .RequirePermission(CatalogPermissions.PeopleRead)
            .Produces<Page<PersonResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetPersonAsync)
            .WithName("GetPerson")
            .WithSummary("Returns a person.")
            .RequirePermission(CatalogPermissions.PeopleRead)
            .Produces<PersonResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreatePerson")
            .WithSummary("Adds a person.")
            .RequirePermission(CatalogPermissions.PeopleWrite)
            .Produces<CreatedResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdatePerson")
            .WithSummary("Changes a person.")
            .RequirePermission(CatalogPermissions.PeopleWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeletePerson")
            .WithSummary("Deletes a person nothing credits.")
            .RequirePermission(CatalogPermissions.PeopleWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetPeopleAsync(
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageLimits.DefaultPageSize)
        => (await dispatcher.Query(new GetPeopleQuery(search, page, pageSize), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetPersonAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetPersonQuery(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> CreateAsync([FromBody] PersonRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new CreatePersonCommand(request.Name, request.Translations), cancellationToken);

        return result.ToHttpResult(id => TypedResults.Created($"/admin/people/{id}", new CreatedResponse(id)));
    }

    private static async Task<IResult> UpdateAsync(Guid id, [FromBody] PersonRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new UpdatePersonCommand(id, request.Name, request.Translations), cancellationToken)).ToHttpResult();

    private static async Task<IResult> DeleteAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new DeletePersonCommand(id), cancellationToken)).ToHttpResult();
}
