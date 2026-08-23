using System.Net;
using System.Net.Http.Json;
using KubernetesAiAgent.Agent.Api;

namespace KubernetesAiAgent.Tests.Api;

public sealed class ModelsEndpointTests(KubernetesAgentTestFactory factory) : IClassFixture<KubernetesAgentTestFactory>
{
    [Fact]
    public async Task GetModels_ReturnsConfiguredModelId()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/models");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ModelsResponse>();

        Assert.NotNull(body);
        Assert.Equal("list", body.Object);
        var model = Assert.Single(body.Data);
        Assert.Equal("kubernetes-agent", model.Id);
        Assert.Equal("model", model.Object);
        Assert.Equal("local", model.OwnedBy);
    }
}
