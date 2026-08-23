using System.ComponentModel.DataAnnotations;
using KubernetesAiAgent.Agent.Configuration;

namespace KubernetesAiAgent.Tests.Configuration;

public sealed class OpenRouterOptionsTests
{
    [Fact]
    public void Validate_DefaultOptions_Fails_BecauseApiKeyIsMissing()
    {
        var options = new OpenRouterOptions();

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(OpenRouterOptions.ApiKey)));
    }

    [Fact]
    public void Validate_WithApiKey_Succeeds()
    {
        var options = new OpenRouterOptions { ApiKey = "sk-or-test" };

        var results = Validate(options);

        Assert.Empty(results);
    }

    [Fact]
    public void Validate_MissingModel_Fails()
    {
        var options = new OpenRouterOptions { ApiKey = "sk-or-test", Model = "" };

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(OpenRouterOptions.Model)));
    }

    [Fact]
    public void Validate_MissingEndpoint_Fails()
    {
        var options = new OpenRouterOptions { ApiKey = "sk-or-test", Endpoint = "" };

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(OpenRouterOptions.Endpoint)));
    }

    private static List<ValidationResult> Validate(OpenRouterOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
