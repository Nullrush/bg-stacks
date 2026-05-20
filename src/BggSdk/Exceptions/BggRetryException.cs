namespace BggSdk.Exceptions;

public sealed class BggRetryException : BggApiException
{
    public BggRetryException(int attempts)
        : base($"BGG API returned HTTP 202 after {attempts} attempt(s). The request was never fulfilled.") { }
}
