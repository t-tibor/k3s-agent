using System.ComponentModel.DataAnnotations;
using KubernetesAiAgent.Agent.Configuration;

namespace KubernetesAiAgent.Tests.Configuration;

public sealed class AgentOptionsTests
{
    [Fact]
    public void Validate_DefaultOptions_Succeeds()
    {
        var options = new AgentOptions();

        var results = Validate(options);

        Assert.Empty(results);
    }

    [Fact]
    public void Validate_MissingModelId_Fails()
    {
        var options = new AgentOptions { ModelId = "" };

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AgentOptions.ModelId)));
    }

    private static List<ValidationResult> Validate(AgentOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
