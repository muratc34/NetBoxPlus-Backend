namespace Shared.Application.Cqrs;

/// <summary>A request that reads state without changing it.</summary>
public interface IQuery<TResponse>;
