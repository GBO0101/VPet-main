using System.Linq;
using VPet_Simulator.Windows.AiAgent;
using Xunit;

namespace VPet_Simulator.Windows.Tests;

public sealed class AiAgentModelCatalogTests
{
    [Fact]
    public void ParsesOllamaTags()
    {
        var models = AiModelCatalogService.ParseOllamaTags("""{"models":[{"name":"qwen3:8b"},{"name":"gemma3:4b"}]}""");

        Assert.Equal(new[] { "qwen3:8b", "gemma3:4b" }, models.ToArray());
    }

    [Fact]
    public void ParsesRemoteModels()
    {
        var models = AiModelCatalogService.ParseRemoteModels("""{"data":[{"id":"model-a"},{"id":"model-b"}]}""");

        Assert.Equal(new[] { "model-a", "model-b" }, models.ToArray());
    }
}
