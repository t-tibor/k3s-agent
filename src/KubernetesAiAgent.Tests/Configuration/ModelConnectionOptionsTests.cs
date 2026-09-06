using System.ComponentModel.DataAnnotations;
using KubernetesAiAgent.NetAgent.Configuration;

namespace KubernetesAiAgent.Tests.Configuration;

public sealed class ModelConnectionOptionsTests
{
    [Fact]
    public void Validate_DefaultOptions_Fails_BecauseApiKeyIsMissing()
    {
        var options = new ModelConnectionOptions();

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ModelConnectionOptions.ApiKey)));
    }

    [Fact]
    public void Validate_WithApiKey_Succeeds()
    {
        var options = new ModelConnectionOptions { ApiKey = "sk-or-test" };

        var results = Validate(options);

        Assert.Empty(results);
    }

    [Fact]
    public void Validate_MissingModel_Fails()
    {
        var options = new ModelConnectionOptions { ApiKey = "sk-or-test", Model = "" };

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ModelConnectionOptions.Model)));
    }

    [Fact]
    public void Validate_MissingEndpoint_Fails()
    {
        var options = new ModelConnectionOptions { ApiKey = "sk-or-test", Endpoint = "" };

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ModelConnectionOptions.Endpoint)));
    }

    private static List<ValidationResult> Validate(ModelConnectionOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
