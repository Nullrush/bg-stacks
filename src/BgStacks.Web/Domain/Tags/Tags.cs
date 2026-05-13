namespace BgStacks.Web.Domain.Tags;

public sealed record Tags(int[] Want, int[] Played)
{
    public static Tags Empty => new([], []);
}
