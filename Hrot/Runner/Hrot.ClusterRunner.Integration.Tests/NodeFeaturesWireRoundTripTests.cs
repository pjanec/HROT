using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐ Node Features CE-4215 ⑤ — the REAL DDS round-trip of the S1 wire types, which the unit rails only prove through their converters:
/// the operator's <c>SetRunSettings</c> override travels master → <c>ClusterStateTopic.FeatureOverrides</c> (<c>FeatureOverrideWire</c>) → the named node,
/// and that node's <c>NodeHeartbeat.FeaturesOn</c> travels back to the master's roster.
/// 📄 docs/DESIGN_Node_Features.md §7.1 ③ / ⑤, §8.
/// 🔴 Mutations: the master not copying the overrides onto the topic ⇒ the IG never turns the feature off; the heartbeat not writing / reading
/// <c>FeaturesOn</c> ⇒ the roster cell stays empty (or stale).
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class NodeFeaturesWireRoundTripTests
{
    // Domain 45: free (the harness suites use 52+ / 71+ …; CycloneDDS ports are 7400 + 250 × domain, so it must stay below ~232).
    private const int DomainId = 45;
    private const string Feature = "map3d.in-load";

    [Fact(Timeout = 90_000)]
    public async Task AClusterOverrideForOneNode_ReachesThatNode_AndItsHeartbeatFeaturesOnChanges()
    {
        using var harness = new HrotRunnerHarness("simhost,player,cgf", DomainId);
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;

        // the IG joined the roster and its heartbeat already lists the feature it has ON by default (the DDS leg node → master)
        Hrot.Orchestrator.NodeHealthProfile? PlayerProfile() =>
            master.NodeRoster.ActiveNodes.Values.FirstOrDefault(p => p.SubsystemName.StartsWith("Ig", StringComparison.OrdinalIgnoreCase)   /* the IG heartbeat names its application: "IgApplication" */);
        Assert.True(harness.PumpUntil(() => PlayerProfile()?.FeaturesOn.Contains(Feature) == true, timeoutFrames: 2000),
            "the Player's heartbeat must carry FeaturesOn including " + Feature + " over DDS. Roster: " +
            string.Join(", ", master.NodeRoster.ActiveNodes.Values.Select(p => $"{p.SubsystemName}[{string.Join("|", p.FeaturesOn)}]")));
        string playerName = PlayerProfile()!.NodeName;
        var playerFeatures = harness.Player.App.NodeContext!.Features!;
        Assert.True(playerFeatures.On(Feature));

        // the operator turns it off for the IG node only (the JSON cluster-op channel, as the editor / HTTP send it)
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.SetRunSettings,
            PayloadJson   = JsonSerializer.Serialize(new
            {
                FeatureOverrides = new[] { new { Feature, Nodes = new[] { playerName }, Json = "{\"Enabled\":false}" } },
            }),
        }).ConfigureAwait(false);

        // the cluster fact reaches the node (master → ClusterStateTopic.FeatureOverrides → the node's ClusterView → NodeFeatures) …
        Assert.True(harness.PumpUntil(() => !playerFeatures.On(Feature), timeoutFrames: 2000),
            "the Player node must apply the cluster override that names it (FeatureOverrideWire over DDS).");
        // … and its next heartbeat reports the change back
        Assert.True(harness.PumpUntil(() => PlayerProfile()?.FeaturesOn.Contains(Feature) == false, timeoutFrames: 2000),
            "the Player's heartbeat FeaturesOn must stop listing " + Feature + " after the override. Now: [" + string.Join("|", PlayerProfile()?.FeaturesOn ?? Array.Empty<string>()) + "]");
        Assert.Empty(PlayerProfile()!.FeatureErrors);   // a valid override is no error
    }
}
