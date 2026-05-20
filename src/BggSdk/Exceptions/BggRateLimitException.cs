namespace BggSdk.Exceptions;

public sealed class BggRateLimitException : BggApiException
{
    public BggRateLimitException()
        : base("BGG API rate limit exceeded (HTTP 429).") { }
}
