using Kode.Agent.Sdk.Core.Skills;
using Xunit;

namespace Kode.Agent.Tests.Unit;

public class SkillsConfigAutoActivateTests
{
    [Fact]
    public void SkillsConfig_AutoActivate_defaults_to_null()
    {
        var config = new SkillsConfig { Paths = [] };

        Assert.Null(config.AutoActivate);
    }

    [Fact]
    public void SkillsConfig_AutoActivate_can_be_set()
    {
        var config = new SkillsConfig
        {
            Paths = [],
            AutoActivate = ["koda-workspace", "koda-memory"],
        };

        Assert.Equal(2, config.AutoActivate!.Count);
        Assert.Contains("koda-workspace", config.AutoActivate);
        Assert.Contains("koda-memory", config.AutoActivate);
    }

    [Fact]
    public void SkillsConfig_AutoActivate_empty_list_does_not_trigger_auto_activation()
    {
        var config = new SkillsConfig
        {
            Paths = [],
            AutoActivate = [],
        };

        // Count == 0, so the Agent.cs guard `is { Count: > 0 }` will skip activation
        Assert.Empty(config.AutoActivate!);
    }
}
