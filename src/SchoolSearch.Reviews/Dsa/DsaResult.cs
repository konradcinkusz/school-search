namespace SchoolSearch.Reviews.Dsa;

/// <summary>A refused operation: HTTP status, stable code and a message that never echoes user text.</summary>
public sealed record DsaFailure(int Status, string Code, string Message);

/// <summary>Either a value or a failure. Services return this; endpoints map it to HTTP.</summary>
public sealed record DsaResult<T>(T? Value, DsaFailure? Failure) where T : class
{
    public static DsaResult<T> Ok(T value) => new(value, null);

    public static DsaResult<T> Fail(int status, string code, string message) =>
        new(null, new DsaFailure(status, code, message));
}
