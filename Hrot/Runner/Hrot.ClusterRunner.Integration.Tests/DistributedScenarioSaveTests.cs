using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;

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
    // Domain range 81-89 (free). ⛔ It was 271: CycloneDDS's default port mapping is 7400 + 250 × domain, which must
    // stay below 65536 ⇒ domains above ~232 cannot create a participant ("Failed to create participant") — so this test
    // could never run at all.
    private const int DomainBase = 81;
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

    /// <summary>
    /// ⭐⭐ CE-4091 (AQ108 D7, M2/M3) — a recording made after EDIT → distributed SAVE → GO-LIVE keeps the scenario as SAVED: the master's copy of the
    /// scenario folder holds the merged <c>scenario.json</c> byte for byte, the <c>foreign/</c> subfolder the save routed (ExCon's slice), and a
    /// stamp whose hash is the SAVED file's — not the recipe's that was loaded first. (The copy and stamp are made at go-live by the master's
    /// inventory; the unit rails in <c>AssetInventoryProcessManagerTests</c> pin each rule, this one proves the real cluster path reaches them.)
    /// </summary>
    [Fact]
    public async Task CE4091_ARecordingAfterEditSaveGoLive_CarriesTheSavedFilesHash_AndEverySubfolder()
    {
        string repoRoot = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(repoRoot, "Hrot", "Subsystems")) && Directory.GetParent(repoRoot) is { } up) repoRoot = up.FullName;
        var recipe = Path.Combine(repoRoot, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Scenarios", "tt-take-cover", "scenario.json");
        Assert.True(File.Exists(recipe), "the shipped recipe is the scenario that gets loaded, then saved over: " + recipe);
        Directory.CreateDirectory(ScenarioDir);
        File.Copy(recipe, Path.Combine(ScenarioDir, "scenario.json"), overwrite: true);
        var loadedHash = Fdp.Toolkit.Scenario.ScenarioDescription.HashOf(File.ReadAllText(Path.Combine(ScenarioDir, "scenario.json")));
        var exercise = Guid.NewGuid();

        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", NextDomainId());
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        Assert.True(harness.PumpUntil(() => master.NodeRoster.ActiveNodes.Count >= 2, timeoutFrames: 400), "cluster roster must populate");

        // load for editing → the loaded hash is the recipe's
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            PayloadJson = new JsonObject { ["TargetState"] = ClusterState.OperatingEdit.ToString(), ["ScenarioId"] = _name }.ToJsonString(),
        });
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == (int)ClusterState.OperatingEdit, timeoutFrames: 4000), "the cluster must open the scenario for editing");

        // save: the nodes' slices are merged over scenario.json and ExCon's goes to foreign/
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.SaveScenario,
            PayloadJson = new JsonObject { ["ScenarioName"] = _name }.ToJsonString(),
        });
        var saved = Path.Combine(ScenarioDir, "scenario.json");
        Assert.True(harness.PumpUntil(() => Directory.Exists(Path.Combine(ScenarioDir, "foreign"))
            && Fdp.Toolkit.Scenario.ScenarioDescription.HashOf(File.ReadAllText(saved)) != loadedHash, timeoutFrames: 800), "the save must rewrite scenario.json and route foreign/");
        var savedText = File.ReadAllText(saved);
        var savedHash = Fdp.Toolkit.Scenario.ScenarioDescription.HashOf(savedText);

        // go live (from Edit the planner goes through Idle and loads the file)
        //   ⚠ as the editor asks (and the DDS translator republishes it): the INTENT on the master's bus — the inventory learns the scenario from it
        //   (a direct HandleClusterOpRequestAsync call bypasses the bus, so the inventory would see a live run with no scenario named)
        harness.OrchestratorSvc.TimeBusForTest!.PublishManaged(new Fdp.Toolkit.Orchestration.TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(), TargetState = Fdp.Toolkit.Orchestration.ClusterState.OperatingLive, ScenarioId = _name, ExerciseId = exercise,
        });
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == (int)ClusterState.OperatingLive, timeoutFrames: 6000), "the cluster must go live");
        harness.PumpFrames(30);

        var copyDirs = Directory.EnumerateDirectories(OrchestrationConstants.ResolveStagingRoot(), $"{exercise}.scenario", SearchOption.AllDirectories).ToList();
        var copy = Assert.Single(copyDirs);   // the master's ledger holds the copy while the recording is unarchived
        try
        {
            Assert.Equal(savedText, File.ReadAllText(Path.Combine(copy, "scenario.json")));
            Assert.True(File.Exists(Path.Combine(copy, "foreign", "index.json")), "foreign/ is part of the copy");
            var stamp = Fdp.Toolkit.Scenario.RecordedScenarioStamp.Read(copy);
            Assert.NotNull(stamp);
            Assert.Equal(savedHash, stamp!.ScenarioHash);
            Assert.NotEqual(loadedHash, stamp.ScenarioHash);
            Assert.Equal(_name, stamp.ScenarioName);
        }
        finally
        {
            // the run's staging leaves nothing behind for the next test (the ledger entry and its copy are the master's, under the shared staging root)
            foreach (var d in copyDirs) { try { Directory.Delete(d, recursive: true); } catch { } }
        }
    }
}
