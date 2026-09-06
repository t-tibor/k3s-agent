using System.ComponentModel.DataAnnotations;
using KubernetesAiAgent.NetAgent.Configuration;

namespace KubernetesAiAgent.Tests.Configuration;

public sealed class AgentOptionsTests
{
    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        var options = ValidOptions();

        var results = Validate(options);

        Assert.Empty(results);
    }

    [Fact]
    public void Validate_MissingModelId_Fails()
    {
        var options = ValidOptions();
        options.ModelId = "";

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AgentOptions.ModelId)));
    }

    [Fact]
    public void Validate_MissingModelConnectionApiKey_Fails()
    {
        var options = ValidOptions();
        options.ModelConnection.ApiKey = null;

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains("ModelConnection.ApiKey"));
    }

    [Fact]
    public void Validate_McpServerWithBlankName_Fails()
    {
        var options = ValidOptions();
        options.McpServers.Add(new McpServerOptions { Name = "", Endpoint = "http://example" });

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Any(m => m.EndsWith(".Name")));
    }

    [Fact]
    public void Validate_DuplicateMcpServerNames_Fails()
    {
        var options = ValidOptions();
        options.McpServers.Add(new McpServerOptions { Name = "k8s-mcp", Endpoint = "http://a" });
        options.McpServers.Add(new McpServerOptions { Name = "k8s-mcp", Endpoint = "http://b" });

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Any(m => m.EndsWith(".Name")));
    }

    private static AgentOptions ValidOptions() => new()
    {
        ModelConnection = new ModelConnectionOptions { ApiKey = "sk-or-test" },
    };

    private static List<ValidationResult> Validate(AgentOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
