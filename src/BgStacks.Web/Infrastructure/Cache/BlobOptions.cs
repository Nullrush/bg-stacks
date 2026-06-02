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
        if (string.IsNullOrWhiteSpace(ConnectionString) && string.IsNullOrWhiteSpace(ServiceUri))
            yield return new ValidationResult(
                "Either Blob:ConnectionString or Blob:ServiceUri must be configured.",
                [nameof(ConnectionString), nameof(ServiceUri)]);

        if (string.IsNullOrWhiteSpace(CacheContainer))
            yield return new ValidationResult(
                "Blob:CacheContainer must not be empty.",
                [nameof(CacheContainer)]);
    }
}
