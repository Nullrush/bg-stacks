namespace BgStacks.Web.Domain.Tags;

public sealed class UserTags
{
    public UserId UserId { get; }
    public Tags Tags { get; }

    public UserTags(UserId userId, Tags tags)
    {
        UserId = userId;
        Tags = tags;
    }
}
