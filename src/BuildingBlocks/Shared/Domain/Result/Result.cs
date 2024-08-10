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
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);


    public static Result Failure(Error error) => new Result(false, error);
    public static Result<TValue> Failure<TValue>(Error error) => new(default!, false, error);

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

public class Result<TValue> : Result
{
    private readonly TValue _value;

    protected internal Result(TValue value, bool isSuccess, Error? error)
        : base(isSuccess, error)
        => _value = value;

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public TValue? Value => IsSuccess
        ? _value
        : default;
}