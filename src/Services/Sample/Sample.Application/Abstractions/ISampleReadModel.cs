using Sample.Domain.Notes;

namespace Sample.Application.Abstractions;

/// <summary>
/// Untracked queryable views for query handlers, which project straight to response types.
/// Soft-deleted rows are already filtered out.
/// </summary>
public interface ISampleReadModel
{
    IQueryable<Note> Notes { get; }
}
