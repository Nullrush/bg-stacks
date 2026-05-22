using System.ComponentModel.DataAnnotations;

namespace BgStacks.Web.Infrastructure.Cache;

public class BlobOptions : IValidatableObject
{
    public static string SectionName => "Blob";

    public string? ConnectionString { get; set; }
    public string? ServiceUri { get; set; }
    public string CacheContainer { get; set; } = "cache";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ConnectionString is null && ServiceUri is null)
            yield return new ValidationResult(
                "Either Blob:ConnectionString or Blob:ServiceUri must be configured.",
                [nameof(ConnectionString), nameof(ServiceUri)]);
    }
}
