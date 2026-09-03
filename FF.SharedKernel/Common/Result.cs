namespace FF.SharedKernel.Common;

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("Success result cannot have an error.");
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("Failure result must have an error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result<T> Success<T>(T value) => new(value, true, Error.None);
    public static Result<T> Failure<T>(Error error) => new(default, false, error);
}

public class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, bool isSuccess, Error error) : base(isSuccess, error)
    {
        _value = value;
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access value of a failed result.");

    // ── Why these exist ──────────────────────────────────────────────────────
    //
    // Static members are inherited in C#, so before these were here,
    // `Result<Foo>.Failure(error)` did NOT resolve to anything generic. The two
    // candidates on the base are:
    //
    //     Result    Failure(Error error)        <- no T anywhere
    //     Result<U> Failure<U>(Error error)     <- U cannot be inferred from Error
    //
    // Overload resolution therefore picked the NON-generic one and handed back a
    // base `Result`. That does not compile where a `Result<Foo>` is expected, so
    // the natural next move is to add a cast to make the red squiggle go away —
    // and `(Result<Foo>)someResult` compiles cleanly, because it is a legal
    // downcast on paper. At runtime it throws InvalidCastException every single
    // time, because the object really is a plain `Result`.
    //
    // Found 2026-09-02 in SyncNflverseDraftPicksCommandHandler, where both casts
    // sat on error paths — including the one inside `catch`, so the failure only
    // ever surfaced while something else was already going wrong, and it replaced
    // the original exception on its way out.
    //
    // Declaring the pair here means the derived name wins for any call through
    // `Result<T>`, so the trap cannot be re-entered. Existing `(Result<Foo>)`
    // casts at call sites become redundant identity casts rather than landmines,
    // so this fix is safe to make without touching them first.
    public static new Result<T> Success(T value) => new(value, true, Error.None);
    public static new Result<T> Failure(Error error) => new(default, false, error);

    public static implicit operator Result<T>(T value) => new(value, true, Error.None);
}
