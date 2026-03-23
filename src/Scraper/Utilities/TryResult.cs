using System.Diagnostics.CodeAnalysis;

namespace SiteChecker.Scraper.Utilities;

public class TryResult<T>
{
    [MemberNotNullWhen(true, nameof(Result))]
    [MemberNotNullWhen(false, nameof(Exception))]
    public required bool IsSuccess { get; init; }

    public T? Result { get; init; }

    public Exception? Exception { get; init; }
}

public class TryResult
{
    public static TryResult<T> Success<T>(T result) => new()
    {
        IsSuccess = true,
        Result = result,
        Exception = null,
    };

    public static TryResult<T> Failure<T>(Exception exception) => new()
    {
        IsSuccess = false,
        Result = default,
        Exception = exception,
    };
}
