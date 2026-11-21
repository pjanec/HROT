using Fdp.Testing;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐ O-4 part 2 (CE-4415, docs/DESIGN_Terrain_Authoring.md §18c) — a scenario WITH a compiled terrain overlay loads on every node: the prefetch stages <c>Terrain/overlays/&lt;hash&gt;/</c> beside the
/// terrain, each node's terrain step merges it, and the house the scenario authored (a <c>TerrainFeature</c>, compiled at save) is in the resident <c>TerrainWorld</c> of SimHost, with its door.
/// 🔴 Red-proof: have <c>TerrainLoadStep</c> ignore <c>OverlayHash</c> ⇒ the house is missing on every node.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class TerrainOverlayClusterLoadTests : IDisposable
{
    private const int DomainBase = 120;   // CycloneDDS accepts 0–232
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly string _scenarioId = "tt_overlay_" + Guid.NewGuid().ToString("N");
    private const string House = "a1000000-0000-4000-8000-0000000000aa";

    private string? _stagedHeader, _stagedOverlay, _savedHeader;

    public void Dispose()
    {
        NasScenarioStaging.Remove(_scenarioId);
        try { if (_stagedOverlay != null && Directory.Exists(_stagedOverlay)) Directory.Delete(_stagedOverlay, true); } catch { }
        try
        {
            if (_stagedHeader != null) { if (_savedHeader != null) File.WriteAllText(_stagedHeader, _savedHeader); else File.Delete(_stagedHeader); }
        }
        catch { }
    }

    [Fact(Timeout = 240_000)]
    public async Task AScenarioWithATerrainOverlay_LoadsItOnEveryNode()
    {
        var dir = NasScenarioStaging.DirectoryOf(_scenarioId);
        Directory.CreateDirectory(dir);
        var recipe = Path.Combine(ContentLayout.ScenarioRecipes(ContentLayout.TryFindSourceTree()!), "tt-take-cover", "scenario.json");
        var doc = JsonNode.Parse(File.ReadAllText(recipe))!.AsObject();
        File.WriteAllText(Path.Combine(dir, "scenario.json"), doc.ToJsonString());

        // the save's compile, as the master runs it: one authored house over test-town, resolved through the catalog the nodes read
        var dom = new JsonObject
        {
            ["header"] = new JsonObject { ["terrainName"] = "test-town" },
            ["entities"] = new JsonObject
            {
                [House] = new JsonObject
                {
                    ["TerrainFeature"] = new JsonObject { ["Kind"] = 1, ["Template"] = "house-2f", ["HeadingDeg"] = 0, ["Pad"] = 0 },
                    ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(260, 40, 0) },
                },
            },
        };
        var catalog = TerrainCatalog.ForNode(localStagingRoot: null, nasRoot: ContentLayout.NasRoot);
        Assert.True(OverlayCompiler.Compile(dom, dir, catalog.ResolveDefinition, bakeNavtiles: false).Wrote);

        // ⚠ The in-process nodes share ONE staging root (OrchestrationConstants.ResolveStagingRoot) while the gateway stages to per-node folders — so the harness cannot exercise the
        //   distribution itself (StorageGatewayTests O-4 does). What runs here is the NODE half, on a real cluster: the staged header and the staged overlay folder are put where the nodes read
        //   them, exactly as the gateway writes them (BuildStagedHeaderJson, AssetTreeSync into Terrain/overlays/<hash>), and removed again afterwards.
        var overlayHeader = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, OverlayCompiler.FolderName, "overlay.json")))!;
        string hash = (string)overlayHeader["contentHash"]!;
        string nodeRoot = OrchestrationConstants.ResolveStagingRoot();
        _stagedOverlay = Path.Combine(nodeRoot, TerrainCatalog.StagingDirectoryName, TerrainCatalog.OverlaysDirectoryName, hash);
        new Fdp.Toolkit.Orchestration.Assets.AssetTreeSync().Sync(Path.Combine(dir, OverlayCompiler.FolderName), _stagedOverlay);
        _stagedHeader = Path.Combine(OrchestrationConstants.GetTkbStagingRoot(nodeRoot), "ScenarioHeader.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_stagedHeader)!);
        if (File.Exists(_stagedHeader)) _savedHeader = File.ReadAllText(_stagedHeader);
        File.WriteAllText(_stagedHeader, Hrot.Orchestrator.StorageGatewayModule.BuildStagedHeaderJson(new Hrot.Orchestrator.StagedArtifactNames("test-town", hash)));

        { // what a node's load does, once, here: the failure (if any) would otherwise be swallowed by the cluster's load status
            using var holder = new CarKinem.Road.RoadNetworkHolder();
            var residency = new Hrot.Map.Common.Services.TerrainResidency(nodeRoot, holder);
            var folder = residency.ResolveOverlay(hash);
            Assert.NotNull(folder);
            var staged = residency.Prepare("test-town", folder);
            Assert.Contains(staged.World!.Buildings, b => b.Label == House + "/0");
        }

        using var harness = new HrotRunnerHarness("simhost,player,cgf", NextDomainId());
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline) { harness.PumpFrames(1); Thread.Sleep(10); }
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingLive), ScenarioId = _scenarioId }),
        }).ConfigureAwait(false);
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000), $"cluster must reach OperatingLive; at {(int)master.CurrentClusterState}");

        // ⚠ measured: CGF (the Brain node) keeps no resident TerrainWorld singleton in this harness (waited 2000 frames) — only SimHost, the Muscle, does; so SimHost is the node asserted
        foreach (var (tag, world) in new[] { ("simhost", harness.SimHost.World!) })
        {
            Assert.True(harness.PumpUntil(() => world.HasSingletonManaged<TerrainWorld>(), timeoutFrames: 2000), $"{tag}: test-town must be resident");
            var town = world.GetSingletonManaged<TerrainWorld>();
            Assert.True(town.Buildings.Any(b => b.Label == House + "/0"), $"{tag}: {town.Buildings.Count} buildings, {town.Prisms.Count} prisms, source {town.SourceFolder}, seeds [{string.Join(",", town.SeedFolders)}]");
            Assert.Contains(town.Doors, d => d.Key.Contains(House + "/0/"));
        }
    }
}
