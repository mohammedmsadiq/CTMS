namespace CTMS.AdminUI.ApiContracts;

/// <summary>
/// A normalised API failure. Built from an RFC 7807 <c>problem+json</c> body when the
/// server sent one, otherwise synthesised from the transport error / status code.
/// </summary>
public sealed record ApiError(int Status, string Title, string? Detail)
{
    public string Message => string.IsNullOrWhiteSpace(Detail) ? Title : Detail!;

    public static ApiError Transport(string detail) =>
        new(0, "Cannot reach the CTMS API", detail);

    public static ApiError FromStatus(int status, string? detail = null) =>
        new(status, ReasonFor(status), detail);

    private static string ReasonFor(int status) => status switch
    {
        400 => "Invalid request",
        401 => "Not authenticated",
        403 => "Not authorised",
        404 => "Resource not found",
        409 => "Conflict",
        >= 500 => "Server error",
        _ => $"Request failed ({status})",
    };
}

/// <summary>Thrown by the API client for callers that prefer exceptions over <see cref="Result{T}"/>.</summary>
public sealed class ApiException(ApiError error)
    : Exception(error.Message)
{
    public ApiError Error { get; } = error;
}

/// <summary>Result of a call with no payload (e.g. DELETE).</summary>
public readonly struct Result
{
    private Result(bool ok, ApiError? error)
    {
        IsSuccess = ok;
        Error = error;
    }

    public bool IsSuccess { get; }
    public ApiError? Error { get; }
    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(true, null);
    public static Result Failure(ApiError error) => new(false, error);
}

/// <summary>Result of a call that returns <typeparamref name="T"/> on success.</summary>
public readonly struct Result<T>
{
    private Result(bool ok, T? value, ApiError? error)
    {
        IsSuccess = ok;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }
    public T? Value { get; }
    public ApiError? Error { get; }
    public bool IsFailure => !IsSuccess;

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(ApiError error) => new(false, default, error);
}
