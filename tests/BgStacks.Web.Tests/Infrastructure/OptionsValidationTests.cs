using BgStacks.Web.Infrastructure.Cache;
using BgStacks.Web.Infrastructure.Events;
using FluentAssertions;
using System.ComponentModel.DataAnnotations;

namespace BgStacks.Web.Tests.Infrastructure;

public class OptionsValidationTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(null, "")]
    [InlineData(null, "  ")]
    public void CosmosOptions_NullOrWhitespaceConnectionAndEndpoint_YieldsValidationError(
        string? connectionString, string? endpoint)
    {
        var options = new CosmosOptions { ConnectionString = connectionString, Endpoint = endpoint };
        var results = options.Validate(new ValidationContext(options)).ToList();
        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(CosmosOptions.ConnectionString));
    }

    [Fact]
    public void CosmosOptions_ConnectionStringSet_IsValid()
    {
        var options = new CosmosOptions { ConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=test==" };
        var results = options.Validate(new ValidationContext(options)).ToList();
        results.Should().BeEmpty();
    }

    [Fact]
    public void CosmosOptions_EndpointSet_IsValid()
    {
        var options = new CosmosOptions { Endpoint = "https://myaccount.documents.azure.com:443/" };
        var results = options.Validate(new ValidationContext(options)).ToList();
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(null, "")]
    [InlineData(null, "  ")]
    public void BlobOptions_NullOrWhitespaceConnectionAndServiceUri_YieldsValidationError(
        string? connectionString, string? serviceUri)
    {
        var options = new BlobOptions { ConnectionString = connectionString, ServiceUri = serviceUri };
        var results = options.Validate(new ValidationContext(options)).ToList();
        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(BlobOptions.ConnectionString));
    }

    [Fact]
    public void BlobOptions_ConnectionStringSet_IsValid()
    {
        var options = new BlobOptions { ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=abc==" };
        var results = options.Validate(new ValidationContext(options)).ToList();
        results.Should().BeEmpty();
    }

    [Fact]
    public void BlobOptions_ServiceUriSet_IsValid()
    {
        var options = new BlobOptions { ServiceUri = "https://mystorageaccount.blob.core.windows.net" };
        var results = options.Validate(new ValidationContext(options)).ToList();
        results.Should().BeEmpty();
    }

    [Fact]
    public void CosmosOptions_EmptyDatabaseId_YieldsValidationError()
    {
        var options = new CosmosOptions { ConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=test==", DatabaseId = "" };
        var results = options.Validate(new ValidationContext(options)).ToList();
        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(CosmosOptions.DatabaseId));
    }

    [Fact]
    public void BlobOptions_EmptyCacheContainer_YieldsValidationError()
    {
        var options = new BlobOptions { ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=abc==", CacheContainer = "" };
        var results = options.Validate(new ValidationContext(options)).ToList();
        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(BlobOptions.CacheContainer));
    }
}
