namespace Marketplace.Application.Common;

/// <summary>Lightweight result wrapper for service operations (skeleton).</summary>
public class Result
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public static Result Success() => new() { Succeeded = true };
    public static Result Fail(string error) => new() { Succeeded = false, Error = error };
}

public class Result<T> : Result
{
    public T? Value { get; init; }
    public static Result<T> Success(T value) => new() { Succeeded = true, Value = value };
    public static new Result<T> Fail(string error) => new() { Succeeded = false, Error = error };
}
