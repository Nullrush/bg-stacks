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
        if (ConnectionString is null && Endpoint is null)
            yield return new ValidationResult(
                "Either Cosmos:ConnectionString or Cosmos:Endpoint must be configured.",
                [nameof(ConnectionString), nameof(Endpoint)]);
    }
}
