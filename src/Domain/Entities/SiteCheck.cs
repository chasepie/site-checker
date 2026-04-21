using System.Text.Json.Serialization;
using SiteChecker.Domain.Enums;
using SiteChecker.Domain.Exceptions;

namespace SiteChecker.Domain.Entities;

public class SiteCheck : IEntityWithId
{
    private const string ExceptionTypeKey = "EXCEPTION_TYPE";

    public required int Id { get; set; }

    public required string? Value { get; set; }

    public string? VpnLocationId { get; set; }

    public CheckStatus Status { get; set; } = CheckStatus.Created;

    public required DateTime StartDate { get; set; }

    public required DateTime? DoneDate { get; set; }

    public required int SiteId { get; set; }

    public BrowserType? BrowserType { get; set; }

    [JsonIgnore]
    public Dictionary<string, string> Metadata { get; set; } = [];

    [JsonIgnore]
    public bool IsSuccess => Status == CheckStatus.Done;

    [JsonIgnore]
    public bool IsComplete => Status == CheckStatus.Failed || Status == CheckStatus.Done;

    /// <summary>
    /// True when the check failed due to a known/expected exception type (e.g. access denied, blank page).
    /// Known failures do not trigger notifications.
    /// </summary>
    [JsonIgnore]
    public bool IsKnownFailure
    {
        get
        {
            if (Status != CheckStatus.Failed)
            {
                return false;
            }

            if (Metadata.TryGetValue(ExceptionTypeKey, out var exceptionType)
                && !string.IsNullOrEmpty(exceptionType))
            {
                return exceptionType.EndsWith(nameof(KnownScraperException))
                    || exceptionType.EndsWith(nameof(AccessDeniedScraperException))
                    || exceptionType.EndsWith(nameof(BlankPageScraperException));
            }

            return false;
        }
    }

    public SiteCheck() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SiteCheck(int siteId)
    {
        SiteId = siteId;
        Status = CheckStatus.Created;
        StartDate = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the check status and value from a scrape result.
    /// </summary>
    /// <param name="result">The result of the scrape operation.</param>
    public void CompleteWithResult(IScrapeResult result)
    {
        if (result.IsFailure(out var failure))
        {
            Status = CheckStatus.Failed;
            Value = failure.ErrorMessage;
            if (failure.Exception != null)
            {
                var exceptionType = failure.Exception.GetType().FullName;
                if (!string.IsNullOrEmpty(exceptionType))
                {
                    // Create a new dictionary to ensure EF Core detects the change
                    Metadata = new(Metadata) { [ExceptionTypeKey] = exceptionType };
                }
            }
        }
        else if (result.IsSuccess(out var success))
        {
            Status = CheckStatus.Done;
            Value = success.Content;
        }
        else
        {
            throw new InvalidOperationException("Unknown scrape result type");
        }

        DoneDate = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the check as failed due to an unhandled exception.
    /// </summary>
    /// <param name="ex">The exception that caused the failure.</param>
    public void FailWithException(Exception ex)
    {
        Status = CheckStatus.Failed;
        Value = ex.Message;
        DoneDate = DateTime.UtcNow;
    }
}
