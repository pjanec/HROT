using System;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.Common.Infrastructure;
using Xunit;

namespace Hrot.Core.Tests.Infrastructure;

/// <summary>⭐ Node Features S1 (a) — the layering, Resolve-throws and On/Config rails. 📄 docs/DESIGN_Node_Features.md §7.</summary>
public sealed class NodeFeaturesTests
{
    public struct KillCamConfig : IFeatureConfig
    {
        public bool Enabled { get; set; }
        public float Seconds;
        public bool ShowKiller;
    }

    private static FeatureRegistry Registry() => new(new[]
    {
        FeatureDeclaration.Of<KillCamConfig>("hud.kill-cam", "kill cam", FeatureChange.Live,
            r => r == NodeRole.Map2D ? "{\"Enabled\":true,\"Seconds\":5}" : null),
        FeatureDeclaration.Switch("gameplay.team-kill", "team kill", FeatureChange.AtLoad, _ => true),
    });

    [Fact]
    public void TypeDefaultsThenRoleDefault()
    {
        var f = new NodeFeatures(Registry(), NodeRole.Brain);
        Assert.False(f.On("hud.kill-cam"));                   // type default (Enabled=false), no Brain default
        Assert.True(f.On("gameplay.team-kill"));              // role default
        var p = new NodeFeatures(Registry(), NodeRole.Map2D);
        Assert.True(p.On("hud.kill-cam"));
        Assert.Equal(5f, p.Config<KillCamConfig>(p.Resolve("hud.kill-cam")).Seconds);
    }

    [Fact]
    public void NodeOverrideThenClusterOverride_PartialObjectsKeepOtherKeys()
    {
        var f = new NodeFeatures(Registry(), NodeRole.Map2D, () => "Player-2");
        var h = f.Resolve("hud.kill-cam");
        f.SetNodeOverride("hud.kill-cam", "{\"Seconds\":3}");
        Assert.Equal(3f, f.Config<KillCamConfig>(h).Seconds);
        Assert.True(f.On(h));                                  // Enabled kept from the role default
        f.SetClusterOverrides(new[] { new FeatureOverrideSpec("hud.kill-cam", new[] { "Player-2" }, "{\"Seconds\":9,\"Enabled\":false}") });
        Assert.Equal(9f, f.Config<KillCamConfig>(h).Seconds);
        Assert.False(f.On(h));
        f.SetClusterOverrides(new[] { new FeatureOverrideSpec("hud.kill-cam", new[] { "Other" }, "{\"Enabled\":false}") });
        Assert.True(f.On(h));                                  // an override naming another node does not apply; node layer remains
        Assert.Equal(3f, f.Config<KillCamConfig>(h).Seconds);
        f.SetClusterOverrides(new[] { new FeatureOverrideSpec("hud.kill-cam", Array.Empty<string>(), "{\"Enabled\":false}") });
        Assert.False(f.On(h));                                 // empty node list = all nodes
    }

    [Fact]
    public void ResolveOfUndeclaredIdThrows()
    {
        var f = new NodeFeatures(Registry(), NodeRole.Brain);
        Assert.Throws<InvalidOperationException>(() => f.Resolve("nope"));
        Assert.Throws<InvalidOperationException>(() => f.On("nope"));
        Assert.Throws<InvalidOperationException>(() => f.SetNodeOverride("nope", "{}"));
    }

    [Fact]
    public void UnknownKeyIsRefusedByTheNodeAndReported()
    {
        var f = new NodeFeatures(Registry(), NodeRole.Map2D);
        f.SetNodeOverride("hud.kill-cam", "{\"Bogus\":1}");
        Assert.Single(f.Problems);
        Assert.True(f.On("hud.kill-cam"));                     // the layer was skipped, the rest stands
    }

    [Fact]
    public void ConfigOfWrongTypeThrows_AndDuplicateDeclarationThrows()
    {
        var f = new NodeFeatures(Registry(), NodeRole.Brain);
        Assert.Throws<InvalidOperationException>(() => f.Config<KillCamConfig>(f.Resolve("gameplay.team-kill")));
        Assert.Throws<InvalidOperationException>(() => new FeatureRegistry(new[]
        {
            FeatureDeclaration.Switch("a", "", FeatureChange.Live), FeatureDeclaration.Switch("a", "", FeatureChange.Live),
        }));
    }

    [Fact]
    public void AtLoadFreezeTakesTheStampedClusterLayer_LaterBroadcastsReachOnlyLiveFeatures()
    {
        var f = new NodeFeatures(Registry(), NodeRole.Map2D, () => "P");
        f.FreezeAtLoad(new System.Collections.Generic.Dictionary<string, string> { ["gameplay.team-kill"] = "{\"Enabled\":false}" });
        f.SetClusterOverrides(new[]
        {
            new FeatureOverrideSpec("gameplay.team-kill", Array.Empty<string>(), "{\"Enabled\":true}"),
            new FeatureOverrideSpec("hud.kill-cam", Array.Empty<string>(), "{\"Enabled\":false}"),
        });
        Assert.False(f.On("gameplay.team-kill"));              // AtLoad: frozen at the stamped layer
        Assert.False(f.On("hud.kill-cam"));                    // Live: now
        Assert.Equal("{\"Enabled\":false}", f.AtLoadConfigs()["gameplay.team-kill"]);
        f.FreezeAtLoad(new System.Collections.Generic.Dictionary<string, string>());   // the NEXT load, no stamp for it
    }

    [Fact]
    public void ABranchFromARecording_ReappliesTheRecordedConfig_AndRefusesAFeatureTheBuildDoesNotDeclare()
    {
        var f = new NodeFeatures(Registry(), NodeRole.Brain);
        Assert.True(f.On("gameplay.team-kill"));
        f.FreezeRecorded(new System.Collections.Generic.Dictionary<string, string> { ["gameplay.team-kill"] = "{\"Enabled\":false}" });
        Assert.False(f.On("gameplay.team-kill"));              // the recording wins over the role default
        Assert.Throws<InvalidOperationException>(() => f.FreezeRecorded(new System.Collections.Generic.Dictionary<string, string> { ["gone.feature"] = "{}" }));
        Assert.Throws<InvalidOperationException>(() => f.FreezeRecorded(new System.Collections.Generic.Dictionary<string, string> { ["hud.kill-cam"] = "{}" }));   // Live in this build
        Assert.Throws<InvalidOperationException>(() => f.FreezeRecorded(new System.Collections.Generic.Dictionary<string, string> { ["gameplay.team-kill"] = "{\"Lost\":1}" }));   // a key the type lost
        Assert.False(f.On("gameplay.team-kill"));              // a refused branch changes nothing
    }

    [Fact]
    public void AFeatureIdNoNodeOfTheProcessDeclares_IsUnclaimed_ButOneAnotherNodeDeclaresIsFine()   // CE-4215 ①
    {
        var saved = new System.Collections.Generic.List<(string Id, string Json)>(NodeFeatureOverrides.FromCommandLine);
        try
        {
            NodeFeatureOverrides.Set(new[] { NodeFeatureOverrides.Parse("hud.kill-cam=on"), NodeFeatureOverrides.Parse("hud.kil-cam=off") });
            NodeFeatureOverrides.NoteDeclared(new[] { "gameplay.team-kill" });   // node A
            Assert.Equal(new[] { "hud.kill-cam", "hud.kil-cam" }, NodeFeatureOverrides.Unclaimed());
            NodeFeatureOverrides.NoteDeclared(new[] { "hud.kill-cam" });         // node B declares the first
            Assert.Equal(new[] { "hud.kil-cam" }, NodeFeatureOverrides.Unclaimed());   // only the typo remains
        }
        finally { NodeFeatureOverrides.Set(saved); }
    }
}
