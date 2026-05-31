using System.ComponentModel.DataAnnotations;

namespace BgStacks.Web.Infrastructure.Events;

public class CosmosOptions : IValidatableObject
{
    public static string SectionName => "Cosmos";

    public string DatabaseId { get; set; } = "bgstacks";
    public string? ConnectionString { get; set; }
    public string? Endpoint { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString) && string.IsNullOrWhiteSpace(Endpoint))
            yield return new ValidationResult(
                "Either Cosmos:ConnectionString or Cosmos:Endpoint must be configured.",
                [nameof(ConnectionString), nameof(Endpoint)]);

        if (string.IsNullOrWhiteSpace(DatabaseId))
            yield return new ValidationResult(
                "Cosmos:DatabaseId must not be empty.",
                [nameof(DatabaseId)]);
    }
}
