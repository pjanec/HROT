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

    [Fact]
    public void AnOverrideWithAKeyTheConfigTypeLacks_IsRefusedByTheNode_AndReportedAsAProblem()   // CE-4215 ③ (F-26)
    {
        var f = new NodeFeatures(Registry(), NodeRole.Brain, () => "Player-2");
        Assert.Empty(f.ProblemList());
        f.SetClusterOverrides(new[] { new FeatureOverrideSpec("hud.kill-cam", Array.Empty<string>(), "{\"Lost\":1}") });
        var problem = Assert.Single(f.ProblemList());
        Assert.Contains("hud.kill-cam", problem);
        Assert.Contains("Lost", problem);
        Assert.False(f.On("hud.kill-cam"));                    // the refused layer changed nothing
        f.SetClusterOverrides(Array.Empty<FeatureOverrideSpec>());
        Assert.Empty(f.ProblemList());                          // fixed ⇒ the report clears
    }

    [Fact]
    public void Describe_NamesTheLayerEachValueCameFrom()   // CE-4215 ② (§7.1 ⑥)
    {
        var f = new NodeFeatures(Registry(), NodeRole.Map2D, () => "Player-2");
        f.SetClusterOverrides(new[] { new FeatureOverrideSpec("hud.kill-cam", new[] { "Player-2" }, "{\"Seconds\":3}") });
        var cam = f.Describe()["hud.kill-cam"]!.AsObject();
        var src = cam["source"]!.AsObject();
        Assert.Equal("role default", (string?)src["Enabled"]);       // Map2D role default {"Enabled":true,"Seconds":5}
        Assert.Equal("cluster", (string?)src["Seconds"]);            // the cluster layer wins over the role's 5
        Assert.Equal("type default", (string?)src["ShowKiller"]);
        Assert.Equal(3d, (double)cam["Seconds"]!);
        f.SetNodeOverride("hud.kill-cam", "{\"ShowKiller\":true}");
        Assert.Equal("node", (string?)f.Describe()["hud.kill-cam"]!["source"]!["ShowKiller"]);
    }

    /// <summary>⭐ P-7 F-29/F-30 — the SCENARIO layer sits over the role default and under the node override; "on"/"off" arrive as {"Enabled":…}; an id the registry lacks is skipped and reported (no throw); null clears;
    /// <c>Describe</c> names the layer. ✅ Red-proof: the <c>"scenario"</c> Try line moved after the node override ⇒ the node override no longer wins ⇒ red.</summary>
    [Fact]
    public void ScenarioPreset_SitsOverTheRoleDefault_UnderTheNodeOverride_UnknownIdIsReported_NullClears()
    {
        var f = new NodeFeatures(Registry(), NodeRole.Map2D);
        var kc = f.Resolve("hud.kill-cam");
        Assert.True(f.On(kc));                                                                  // role default
        f.SetScenarioPreset(new System.Collections.Generic.Dictionary<string, string>
        {
            ["hud.kill-cam"] = "{\"Enabled\":false,\"Seconds\":8}",
            ["hud.nope"] = "{\"Enabled\":true}",
        });
        Assert.False(f.On(kc));                                                                 // the scenario beats the role default
        Assert.Equal(8f, f.Config<KillCamConfig>(kc).Seconds);
        Assert.Equal("scenario", (string?)f.Describe()["hud.kill-cam"]!["source"]!["Enabled"]);
        Assert.Single(f.Problems);                                                              // the unknown id: reported, skipped, nothing thrown
        Assert.Contains("hud.nope", f.Problems[0]);
        f.SetNodeOverride("hud.kill-cam", "{\"Enabled\":true}");
        Assert.True(f.On(kc));                                                                  // the node override wins over the scenario
        Assert.Equal(8f, f.Config<KillCamConfig>(kc).Seconds);                                  // …and leaves the scenario's other keys
        Assert.Equal("node", (string?)f.Describe()["hud.kill-cam"]!["source"]!["Enabled"]);
        f.SetScenarioPreset(null);
        Assert.Equal(5f, f.Config<KillCamConfig>(kc).Seconds);                                  // cleared: back to the role default
        Assert.Empty(f.Problems);
        f.SetScenarioPreset(new System.Collections.Generic.Dictionary<string, string> { ["gameplay.team-kill"] = "{bad" });
        Assert.Single(f.Problems);                                                              // malformed JSON: a Problems entry, the rest stands
        Assert.True(f.On("gameplay.team-kill"));
    }

    /// <summary>⭐ Seats H1 — <c>station.seat</c> defaults to the host's seat, and a node override (the launch flag's layer) changes it; an undeclared node reads Author.</summary>
    [Fact]
    public void H1_TheSeatFeature_DefaultsToTheHostsSeat_AndTheLaunchFlagOverridesIt()
    {
        var f = new NodeFeatures(new FeatureRegistry(new[] { StationSeat.Declaration(Seat.Player) }), Fdp.Core.NodeRole.Map2D);
        Assert.Equal(Seat.Player, StationSeat.Of(f));
        f.SetNodeOverride(StationSeat.Id, "{\"Seat\":\"Observer\",\"Side\":\"Blue\"}");
        Assert.Equal(Seat.Observer, StationSeat.Of(f));
        Assert.Equal("Blue", f.Config<SeatConfig>(f.Resolve(StationSeat.Id)).Side.ToString());
        Assert.Empty(f.Problems);
        Assert.Equal(Seat.Author, StationSeat.Of(null));
    }
}
