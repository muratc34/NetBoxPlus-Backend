using Microsoft.EntityFrameworkCore;
using Sample.Application.Abstractions;
using Sample.Domain.Notes;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Sample.Application.Notes.GetNote;

public sealed record GetNoteQuery(Guid Id) : IQuery<NoteResponse>;

public sealed record NoteResponse(Guid Id, string Title, string Content, DateTimeOffset CreatedAt, DateTimeOffset? ModifiedAt);

internal sealed class GetNoteHandler(ISampleReadModel readModel) : IQueryHandler<GetNoteQuery, NoteResponse>
{
    public async Task<Result<NoteResponse>> Handle(GetNoteQuery query, CancellationToken cancellationToken)
    {
        var note = await readModel.Notes
            .Where(n => n.Id == query.Id)
            .Select(n => new NoteResponse(n.Id, n.Title.Value, n.Content, n.CreatedAt, n.ModifiedAt))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return note is null ? NoteErrors.NotFound : note;
    }
}
