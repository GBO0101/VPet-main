using System.Collections.Generic;
using VPet_Simulator.Windows.AiAgent;
using Xunit;

namespace VPet_Simulator.Windows.Tests;

public sealed class AiAgentDiyLinkMatcherTests
{
    [Fact]
    public void FindsCustomLinkByDisplayNameWhenOpenIntentExists()
    {
        var links = new List<(string Name, string Content)>
        {
            ("YT", "https://www.youtube.com/")
        };

        var found = AiAgentDiyLinkMatcher.TryFindTarget("幫我打開 YT", links, out var name, out var content);

        Assert.True(found);
        Assert.Equal("YT", name);
        Assert.Equal("https://www.youtube.com/", content);
    }

    [Fact]
    public void FindsCustomLinkByUrlHostWhenOpenIntentExists()
    {
        var links = new List<(string Name, string Content)>
        {
            ("影片", "https://www.youtube.com/")
        };

        var found = AiAgentDiyLinkMatcher.TryFindTarget("我要看 youtube", links, out var name, out var content);

        Assert.True(found);
        Assert.Equal("影片", name);
        Assert.Equal("https://www.youtube.com/", content);
    }

    [Fact]
    public void IgnoresMentionWithoutOpenIntent()
    {
        var links = new List<(string Name, string Content)>
        {
            ("YT", "https://www.youtube.com/")
        };

        var found = AiAgentDiyLinkMatcher.TryFindTarget("YT 上面有很多影片", links, out _, out _);

        Assert.False(found);
    }
}
