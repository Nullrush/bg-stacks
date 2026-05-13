using System.Security.Cryptography;
using System.Text;

namespace BgStacks.Web.Domain.Tags;

public readonly record struct UserId
{
    public string Value { get; }

    private UserId(string value) => Value = value;

    public static UserId From(string identityProvider, string sub)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(sub);
        var input = $"{identityProvider.ToLowerInvariant()}:{sub}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new UserId(Convert.ToHexString(bytes).ToLowerInvariant());
    }

    public override string ToString() => Value;
}
