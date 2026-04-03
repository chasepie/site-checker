namespace SiteChecker.Domain.Exceptions;

public abstract class ScraperException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{ }

public class UnexpectedScraperException(string message, Exception? innerException = null)
    : ScraperException(message, innerException)
{ }

public class KnownScraperException(string message, Exception? innerException = null)
    : ScraperException(message, innerException)
{ }

public class AccessDeniedScraperException(Exception? innerException = null)
    : KnownScraperException("Access Denied", innerException)
{ }

public class BlankPageScraperException(Exception? innerException = null)
    : KnownScraperException("Blank Page", innerException)
{ }
