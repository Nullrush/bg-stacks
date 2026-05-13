namespace BgStacks.Web.Domain.Tags;

public sealed class ConflictException : Exception
{
    public ConflictException() : base("eTag conflict: the resource has been updated by another client.") { }
}
