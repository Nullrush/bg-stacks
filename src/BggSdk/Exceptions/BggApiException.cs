namespace BggSdk.Exceptions;

public class BggApiException : Exception
{
    public BggApiException(string message) : base(message) { }
    public BggApiException(string message, Exception innerException) : base(message, innerException) { }
}
