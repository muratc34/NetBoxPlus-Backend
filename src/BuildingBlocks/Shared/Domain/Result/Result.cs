namespace Shared.Domain.Result;

public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("Invalid error", nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException("Invalid error", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public Error? Error { get; }

    public static Result Success() => new Result(true, Error.None);
    public static Result<TData> Success<TData>(TData data) => new(data, true, Error.None);


    public static Result Failure(Error error) => new Result(false, error);
    public static Result<TData> Failure<TData>(Error error) => new(default!, false, error);

    public static Result FirstFailureOrSuccess(params Result[] results)
    {
        foreach (Result result in results)
        {
            if (!result.IsSuccess)
            {
                return result;
            }
        }

        return Success();
    }
}

public class Result<TData> : Result
{
    private readonly TData _data;

    protected internal Result(TData data, bool isSuccess, Error? error)
        : base(isSuccess, error)
        => _data = data;

    public static implicit operator Result<TData>(TData data) => Success(data);

    public TData? Data => IsSuccess
        ? _data
        : default;
}