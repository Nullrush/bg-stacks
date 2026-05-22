using BgStacks.Web.Infrastructure.Cache;
using BgStacks.Web.Infrastructure.Events;
using FluentAssertions;
using System.ComponentModel.DataAnnotations;

namespace BgStacks.Web.Tests.Infrastructure;

public class OptionsValidationTests
{
    [Fact]
    public void CosmosOptions_BothNull_YieldsValidationError()
    {
        var options = new CosmosOptions { ConnectionString = null, Endpoint = null };
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

    [Fact]
    public void BlobOptions_BothNull_YieldsValidationError()
    {
        var options = new BlobOptions { ConnectionString = null, ServiceUri = null };
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
}
