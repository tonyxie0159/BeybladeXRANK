namespace BeybladeRecordSystem.Services;

public record ServiceResult(bool Succeeded, string? Error = null)
{
    public static ServiceResult Success() => new(true);
    public static ServiceResult Failure(string error) => new(false, error);
}

public record ServiceResult<T>(bool Succeeded, T? Value = default, string? Error = null)
{
    public static ServiceResult<T> Success(T value) => new(true, value);
    public static ServiceResult<T> Failure(string error) => new(false, default, error);
}
