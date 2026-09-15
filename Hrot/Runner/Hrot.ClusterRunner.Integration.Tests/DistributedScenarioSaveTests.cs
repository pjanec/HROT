using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// CE-277 T-B — proves the DISTRIBUTED scenario SAVE end to end on a live in-process cluster: the
/// SaveScenarioJson fan-out reaches every node, the orchestrator pulls each per-node slice, and
/// <c>ScenarioMergeCore</c> merges the format-compatible ones into ONE canonical <c>scenario.json</c> while
/// routing ExCon's intentionally-incompatible <c>ExCon.Observer</c> slice to <c>foreign/</c> verbatim (§4a–§4c).
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class DistributedScenarioSaveTests : IDisposable
{
    private const int DomainBase = 271;
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly string _name = "ce277_tb_" + Guid.NewGuid().ToString("N");
    private string ScenarioDir =>
        Path.Combine(OrchestrationConstants.GetSharedScenariosRoot(), _name);

    public void Dispose()
    {
        try { if (Directory.Exists(ScenarioDir)) Directory.Delete(ScenarioDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task DistributedSave_MergesCompatibleSlices_AndRoutesExConForeignSlice()
    {
        int domainId = NextDomainId();
        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", domainId);
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;

        // Wait for the node roster (CGF, SimHost, IG, ExCon) to come up.
        Assert.True(
            harness.PumpUntil(() => master.NodeRoster.ActiveNodes.Count >= 2, timeoutFrames: 400),
            "cluster roster must populate");

        // Trigger the distributed JSON save (c0: the ClusterOpRequest path).
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.SaveScenario,
            PayloadJson   = new JsonObject { ["ScenarioName"] = _name }.ToJsonString(),
        });

        // The merge runs async after the pull; pump until the canonical file AND the foreign slice land.
        var canonical = Path.Combine(ScenarioDir, "scenario.json");
        var foreignDir = Path.Combine(ScenarioDir, "foreign");
        bool landed = harness.PumpUntil(
            () => File.Exists(canonical) && Directory.Exists(foreignDir),
            timeoutFrames: 600);

        Assert.True(File.Exists(canonical),
            $"the merged canonical scenario.json must exist at {canonical}");

        // The compatible (Hrot.Scenario) slice(s) merged into the canonical file.
        var dom = JsonNode.Parse(File.ReadAllText(canonical))!.AsObject();
        Assert.Equal("Hrot.Scenario", JsonEnvelope.Read(dom).DocType);

        // ExCon's intentionally-incompatible slice was routed to foreign/, NOT merged.
        Assert.True(landed && Directory.Exists(foreignDir),
            "ExCon's foreign slice must be routed to foreign/");
        var foreignFiles = Directory.GetFiles(foreignDir, "node_*.json");
        Assert.NotEmpty(foreignFiles);
        var foreignDom = JsonNode.Parse(File.ReadAllText(foreignFiles[0]))!.AsObject();
        Assert.Equal("ExCon.Observer", JsonEnvelope.Read(foreignDom).DocType);

        // And an index recorded it for load-side push-back (c3).
        Assert.True(File.Exists(Path.Combine(foreignDir, "index.json")), "foreign index.json must exist");
    }
}
