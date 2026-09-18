using System;
using System.IO;
using System.Text;
using System.Threading;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Core.Orchestration;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.ClusterLoad;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// C5 — the TERRAIN loader. Mirrors <c>TkbLoadClusterStateHandlerTests</c>'s fixture, because the handler
/// mirrors that handler.
///
/// <para>⚠ The <b>rail that matters</b> is not "it loads" — it is
/// <see cref="ANodeWithNoAuthoringDeps_StillGetsTheTerrainLoader"/>: a pure MuscleGround node registers
/// NONE of the scenario-LOAD handlers (they sit behind an authoring-deps conditional), and the muscle is
/// exactly the role that consumes the road network.</para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1e ②a ③ ④.
/// </summary>
public sealed class TerrainLoadStepTests : IDisposable
{
    private readonly string _stagingRoot;
    private readonly string _tkbDir;
    private readonly string _terrainDir;

    public TerrainLoadStepTests()
    {
        _stagingRoot = Path.Combine(Path.GetTempPath(), "TerrainHandlerTest_" + Guid.NewGuid().ToString("N")[..8]);
        _tkbDir      = Path.Combine(_stagingRoot, "TKB");
        _terrainDir  = Path.Combine(_stagingRoot, "Terrain");
        Directory.CreateDirectory(_tkbDir);
        Directory.CreateDirectory(_terrainDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_stagingRoot)) Directory.Delete(_stagingRoot, recursive: true);
    }

    // ── fixture helpers ───────────────────────────────────────────────────────────────────────

    private void WriteScenarioHeader(string? terrainName)
    {
        // The staged header lives beside the TKB artifact — one staged header per node.
        string content = terrainName == null
            ? "{\"SubsystemType\":\"SimHost\"}"
            : $"{{\"$meta\":{{\"docType\":\"Hrot.Scenario\",\"schemaVersion\":1}},\"TerrainName\":\"{terrainName}\"}}";
        File.WriteAllText(Path.Combine(_tkbDir, "ScenarioHeader.json"), content, new UTF8Encoding(false));
    }

    private string WriteTerrainDefinition(string name, string? roadNetworkRelative = null)
    {
        string roads = roadNetworkRelative == null ? "[]" : $"[\"{roadNetworkRelative}\"]";
        string path = Path.Combine(_terrainDir, $"{name}.json");
        File.WriteAllText(path,
            $"{{\"schemaVersion\":1,\"name\":\"{name}\",\"roadNetworks\":{roads}}}",
            new UTF8Encoding(false));
        return path;
    }

    /// <summary>A two-node road network in the JSON shape <c>RoadNetworkLoader</c> reads.</summary>
    private string WriteRoadNetwork(string fileName)
    {
        string path = Path.Combine(_terrainDir, fileName);
        // ⚠ The property names are lowercase on purpose — RoadNetworkJson carries explicit
        //   [JsonPropertyName("nodes")] / ("position") / ("p0") … and System.Text.Json is case-SENSITIVE
        //   by default, so PascalCase here binds to nothing and yields a silently EMPTY network.
        File.WriteAllText(path, """
            {
              "nodes": [
                { "id": 0, "position": { "x": 0.0, "y": 0.0 } },
                { "id": 1, "position": { "x": 100.0, "y": 0.0 } }
              ],
              "segments": [
                {
                  "id": 0,
                  "startNodeId": 0,
                  "endNodeId": 1,
                  "controlPoints": {
                    "p0": { "x": 0.0,   "y": 0.0 },
                    "t0": { "x": 50.0,  "y": 0.0 },
                    "p1": { "x": 100.0, "y": 0.0 },
                    "t1": { "x": 50.0,  "y": 0.0 }
                  },
                  "speedLimit": 13.9, "laneWidth": 3.5, "laneCount": 2
                }
              ]
            }
            """, new UTF8Encoding(false));
        return path;
    }

    private static EntityRepository NewWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterManagedComponent<TerrainDefinition>();
        return repo;
    }

    /// <summary>
    /// ⭐ <c>L3</c> — a step is driven by a LOAD-PHASE CONTEXT. ⚠ <c>terrainName: null</c> means "the
    /// message named none", which sends the step to the staged-header fallback these cases were written
    /// against; a non-null name exercises the message path the orchestrator now uses.
    /// </summary>
    private static LoadPhaseContext Intent(string? terrainName = null) => new(
        TransactionId: Guid.NewGuid(),
        TargetState:   ClusterState.LoadingLive,
        ScenarioId:    "scn",
        TkbName:       null,
        TerrainName:   terrainName,
        ExerciseId:    Guid.Empty,
        IsNewScenario: false);

    /// <summary>
    /// Runs PrepareAsync to completion. ⚠ Deliberately a helper rather than an inline
    /// <c>.GetAwaiter().GetResult()</c> in each test: the handler's prepare is pure file I/O with no
    /// synchronization context, so blocking is safe, and keeping it in one place stops the pattern from
    /// being copied into a test that later gains a real async dependency.
    /// </summary>
    private static void Prepare(TerrainLoadStep handler, LoadPhaseContext intent)
        => handler.PrepareAsync(intent, CancellationToken.None).GetAwaiter().GetResult();

    private (TerrainLoadStep Handler, RoadNetworkHolder Holder) NewHandler()
    {
        var holder = new RoadNetworkHolder();
        return (new TerrainLoadStep(new TerrainResidency(_stagingRoot, holder), _stagingRoot), holder);
    }

    /// <summary>
    /// A handler wired the way PRODUCTION wires it — the world INJECTED, not handed in at commit.
    /// See <see cref="ItPublishesThroughTheINJECTEDWorld_BecauseClusterSlaveAlwaysCommitsWithNullRepo"/>.
    /// </summary>
    /// <summary>
    /// ⚠ The world is no longer injected into the step — <c>LoadPhaseChain</c> owns it and hands it to
    /// every step at commit, which is what makes the null-repo trap impossible to re-introduce per host.
    /// These cases therefore commit through the world explicitly, exactly as the chain does.
    /// </summary>
    private (TerrainLoadStep Handler, RoadNetworkHolder Holder) NewHandlerWithWorld(
        EntityRepository world)
        => NewHandler();

    // ⛔ "it claims the right ops" MOVED to LoadPhaseChainTests: a step claims NOTHING now — the
    //    chain claims once for the whole node, which is the defect fix itself.

    // ── graceful absence ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AScenarioWithNoTerrainName_LoadsAnyway()
    {
        // ⭐ Same contract as TkbName: no opinion is legal, not an error.
        WriteScenarioHeader(terrainName: null);
        var (handler, holder) = NewHandler();
        using var world = NewWorld();
        using (holder)
        {
            var intent = Intent();
            Prepare(handler, intent);
            handler.Commit(intent, world);

            Assert.False(world.HasSingletonManaged<TerrainDefinition>());
            Assert.False(world.HasSingleton<ZoneEnvironmentData>());
        }
    }

    [Fact]
    public void ANamedTerrainWhoseDefinitionIsMissing_FailsLOUDLY()
    {
        // ⛔ Loading the terrain a scenario NAMES is mandatory — a missing definition is a broken
        //    configuration, not a terrain-less scenario, and the two must not look the same.
        WriteScenarioHeader("kandahar");
        var (handler, holder) = NewHandler();
        using (holder)
        {
            var ex = Assert.Throws<FileNotFoundException>(() =>
                Prepare(handler, Intent()));

            Assert.Contains("kandahar", ex.Message);
        }
    }

    // ── the happy path ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ADefinitionListingARoadNetwork_PopulatesZoneEnvironmentData_WithNoZonesSection()
    {
        WriteScenarioHeader("kandahar");
        WriteRoadNetwork("roads.json");
        WriteTerrainDefinition("kandahar", "roads.json");

        var (handler, holder) = NewHandler();
        using var world = NewWorld();
        using (holder)
        {
            var intent = Intent();
            Prepare(handler, intent);

            // ⭐ PrepareAsync must NOT mutate ECS — that is the interface's contract.
            Assert.False(world.HasSingleton<ZoneEnvironmentData>());
            Assert.False(world.HasSingletonManaged<TerrainDefinition>());

            handler.Commit(intent, world);

            Assert.True(world.HasSingleton<ZoneEnvironmentData>());
            var blob = world.GetSingleton<ZoneEnvironmentData>().RoadNetwork;
            Assert.True(blob.Nodes.IsCreated);
            Assert.Equal(2, blob.Nodes.Length);

            var def = world.GetSingletonManaged<TerrainDefinition>()!;
            Assert.Equal("kandahar", def.Name);
            Assert.Single(def.RoadNetworks);
        }
    }

    [Fact]
    public void TheCommittedGraph_IsPublishedIntoTheHolder_SoABackgroundSolverCanSeeIt()
    {
        // ⭐ C7's half: the singleton alone is unreachable from a SlowBackground module.
        WriteScenarioHeader("kandahar");
        WriteRoadNetwork("roads.json");
        WriteTerrainDefinition("kandahar", "roads.json");

        var (handler, holder) = NewHandler();
        using var world = NewWorld();
        using (holder)
        {
            var intent = Intent();
            Prepare(handler, intent);
            handler.Commit(intent, world);

            using var lease = holder.Borrow();
            Assert.True(lease.Value.Nodes.IsCreated);
            Assert.Equal(2, lease.Value.Nodes.Length);
        }
    }

    // ── the differential cache ────────────────────────────────────────────────────────────────

    [Fact]
    public void ReloadingTheSameUnchangedTerrain_DoesNoReIngestion()
    {
        WriteScenarioHeader("kandahar");
        WriteRoadNetwork("roads.json");
        WriteTerrainDefinition("kandahar", "roads.json");

        var (handler, holder) = NewHandler();
        using var world = NewWorld();
        using (holder)
        {
            var first = Intent();
            Prepare(handler, first);
            handler.Commit(first, world);

            // ⭐ The cache key is (name, file timestamp) — unchanged ⇒ the second prepare stages nothing,
            //   so the commit has nothing to publish and the holder does not gain a generation.
            var second = Intent();
            Prepare(handler, second);
            handler.Commit(second, world);

            Assert.Equal(1, holder.LiveGenerations);
        }
    }

    [Fact]
    public void AnAbortedRound_DoesNotAdvanceTheCache_SoTheNextAttemptReIngests()
    {
        WriteScenarioHeader("kandahar");
        WriteRoadNetwork("roads.json");
        WriteTerrainDefinition("kandahar", "roads.json");

        var (handler, holder) = NewHandler();
        using var world = NewWorld();
        using (holder)
        {
            var aborted = Intent();
            Prepare(handler, aborted);
            handler.Abort(aborted);

            Assert.False(world.HasSingleton<ZoneEnvironmentData>());

            // ⛔ Believing a load that never committed is how a node ends up with no road graph and a
            //    cache that says it has one.
            var retry = Intent();
            Prepare(handler, retry);
            handler.Commit(retry, world);

            Assert.True(world.HasSingleton<ZoneEnvironmentData>());
        }
    }

    // ── ⭐⭐⭐ THE RAIL THAT MATTERS ────────────────────────────────────────────────────────────

    [Fact]
    public void ANodeWithNoAuthoringDeps_StillGetsTheTerrainPart()
    {
        // ⛔⛔ The scenario LOAD step is offered only when a host has the full authoring deps
        //    (extractor / source / id-allocator). A pure MuscleGround node passes NONE of them — and the
        //    muscle is precisely the role that consumes the road network. Hanging terrain off the scenario
        //    step would leave it unloaded on the node that needs it most.
        // ⭐ The rail is now a statement about the CHAIN rather than about a registration order: the role
        //   requires the terrain part, so the chain carries it whatever else this host does or does not
        //   compose. 📄 docs/DESIGN_Cluster_Load_Phase.md §4.1b.
        var bootstrapper = new NodeBootstrapper();
        using var world  = new EntityRepository();
        using var kernel = new Fdp.ModuleHost.ModuleHostKernel(world, new EventAccumulator());

        // ⚠ tkbDb is handed in because PRODUCTION hands it in — every ECS node needs a knowledge base,
        //   and composing without one now throws by design. That throw is asserted in LoadPhaseChainTests.
        using var slave = bootstrapper.BuildOrchestration(
            NodeRole.MuscleGround, kernel, world, nodeId: 1,
            participant: null, eventBus: new FdpEventBus(),
            tkbDb: Hrot.Map.Common.HrotEnvironment.CreateTkb());

        Assert.True(slave.IsHandlerRegistered<Hrot.Map.Common.ClusterLoad.LoadPhaseChain>(),
            "every ECS host registers the load-phase chain — it is the ONE participant in the load step");

        var parts = Hrot.Map.Common.ClusterLoad.RoleLoadRequirements.PartsFor(NodeRole.MuscleGround);
        Assert.Contains(Hrot.Map.Common.ClusterLoad.LoadPart.Terrain, parts);
        Assert.Contains(Hrot.Map.Common.ClusterLoad.LoadPart.KnowledgeBase, parts);
        Assert.DoesNotContain(Hrot.Map.Common.ClusterLoad.LoadPart.ScenarioEntities, parts);
    }

    /// <summary>
    /// 🔴 <b>The defect this rail was written against, and why every other test here was blind to it.</b>
    ///
    /// <para>Every test above calls <c>handler.Commit(intent, world)</c> — handing the repository in.
    /// <b><c>ClusterSlave</c> never does that.</b> It commits with <c>repo: null</c> at BOTH of its
    /// dispatch sites (<c>ClusterSlave.cs:271</c> and <c>:432</c>), so the handler as originally written
    /// took its "no-ECS host" branch on EVERY host: it disposed the staged blob and published nothing —
    /// no <c>TerrainDefinition</c>, no <c>ZoneEnvironmentData</c>, no holder swap. A capability that
    /// reports present and silently no-ops (<c>R-133</c>), invisible because the suite exercised a path
    /// production does not take.</para>
    ///
    /// <para>⭐ The fix is the established pattern — <c>repo ?? _world</c>, as
    /// <c>HrotScenarioLoadHandler.cs:196</c> already does. This rail drives commit the way the slave
    /// does, with a NULL repo, and requires the publish to land anyway.</para>
    /// </summary>
    [Fact]
    public void TheCHAINPublishesThroughItsOwnWorld_BecauseClusterSlaveAlwaysCommitsWithNullRepo()
    {
        WriteScenarioHeader("kandahar");
        WriteRoadNetwork("roads.json");
        WriteTerrainDefinition("kandahar", "roads.json");

        using var world  = NewWorld();
        using var holder = new RoadNetworkHolder();

        // ⭐ Driven exactly as production does: the CHAIN holds the world, and the slave commits with a
        //   null repository. ⚠ The step itself no longer holds a world at all — which is the structural
        //   half of the fix: the trap can no longer be re-introduced per host, because there is only one
        //   place left that can fall into it.
        var chain = Hrot.Map.Common.ClusterLoad.LoadPhaseChain.FromRoles(
            NodeRole.MuscleGround,
            new Hrot.Map.Common.ClusterLoad.ILoadPartProvider[]
            {
                new Hrot.Map.Common.ClusterLoad.KnowledgeBaseLoadStep(
                    Hrot.Map.Common.HrotEnvironment.CreateTkb(), _stagingRoot),
                new TerrainLoadStep(new TerrainResidency(_stagingRoot, holder), _stagingRoot),
            },
            world);

        var intent = new ExecuteNodeOpIntent
        {
            Operation     = NodeOpType.PrepareLive,
            TransactionId = Guid.NewGuid(),
            DomainPayload = new Fdp.Toolkit.Orchestration.Handlers.EditLoadHandlerPayload(
                ScenarioId: "scn", TargetState: ClusterState.LoadingLive),
        };

        chain.PrepareAsync(intent, CancellationToken.None).GetAwaiter().GetResult();
        chain.Commit(intent, repo: null);

        Assert.True(world.HasSingleton<ZoneEnvironmentData>(),
            "ClusterSlave commits with repo: null, so the chain must publish through the world it holds");
        Assert.Equal("kandahar", world.GetSingletonManaged<TerrainDefinition>()!.Name);
        Assert.True(world.GetSingleton<ZoneEnvironmentData>().RoadNetwork.Nodes.IsCreated);
    }

    /// <summary>
    /// ⚠ The other half of the same contract: a genuinely no-ECS host (ExCon / CGF skeleton) has no
    /// world to inject, and it must still ACK cleanly rather than throw — it simply has nowhere to
    /// publish, and the staged blob is freed rather than leaked.
    /// </summary>
    [Fact]
    public void ANoEcsHost_WithNoWorldAndNoRepo_StillCommitsCleanly()
    {
        WriteScenarioHeader("kandahar");
        WriteRoadNetwork("roads.json");
        WriteTerrainDefinition("kandahar", "roads.json");

        var (handler, holder) = NewHandler();   // no world injected
        using (holder)
        {
            var intent = Intent();
            Prepare(handler, intent);
            handler.Commit(intent, world: null);   // must not throw

            // ⭐ Still only the holder's initial generation — nothing was published, and the staged blob
            //   was freed rather than leaked. (LiveGenerations is `1 + retired-but-leased`, so 1 is the
            //   floor, not zero.)
            Assert.Equal(1, holder.LiveGenerations);
        }
    }

    // ══ S4 — WHAT TERRAIN INHERITS FROM THE ARTIFACT-STAGING BATCH, AND WHAT IT DOES NOT ═══════════
    //
    // ⭐⭐ S4 was dispatched as a CONFIRMATION, not a port: "terrain mirrors the TKB loader field for
    //    field and should inherit both skips with NO new code; if it needs its own path, that is a
    //    FINDING." 📐 Measured, it is BOTH — and the split is exactly on the header/artifact line.

    /// <summary>
    /// ✅ <b><c>S4</c>, the half that DOES inherit with no new code:</b> the terrain NAME rides the same
    /// staged <c>ScenarioHeader.json</c> the TKB loader reads, so the orchestrator's <c>S2c</c> writer
    /// serves both readers from one file.
    ///
    /// <para>⭐ Asserted through <c>StorageGatewayModule.BuildStagedHeaderJson</c> — the ORCHESTRATOR's
    /// own writer — rather than a hand-written fixture. ⛔ A fixture that writes the header itself could
    /// not catch the orchestrator emitting a shape the node cannot parse, which is the actual risk:
    /// the scenario's own header block is nested and camelCase, and this one is flat and PascalCase.</para>
    /// </summary>
    [Fact]
    public void TheOrchestratorsStagedHeaderIsReadableByTheTerrainReader()
    {
        var json = Hrot.Orchestrator.StorageGatewayModule.BuildStagedHeaderJson(
            new Hrot.Orchestrator.StagedArtifactNames("Alpha_v1", "basic-desert"));
        File.WriteAllText(Path.Combine(_tkbDir, "ScenarioHeader.json"), json, new UTF8Encoding(false));

        Assert.Equal("basic-desert", Fdp.Toolkit.Terrain.ScenarioTerrainName.Read(_stagingRoot));
    }

    /// <summary>
    /// ⭐ A scenario naming a TKB but NO terrain still reads back as "no terrain" — ⛔ not as an empty
    /// string or a throw. ⚠ The no-terrain path stays legal, exactly like the no-TKB one.
    /// </summary>
    [Fact]
    public void AStagedHeaderWithNoTerrainNameReadsBackAsNull()
    {
        var json = Hrot.Orchestrator.StorageGatewayModule.BuildStagedHeaderJson(
            new Hrot.Orchestrator.StagedArtifactNames("Alpha_v1", null));
        File.WriteAllText(Path.Combine(_tkbDir, "ScenarioHeader.json"), json, new UTF8Encoding(false));

        Assert.Null(Fdp.Toolkit.Terrain.ScenarioTerrainName.Read(_stagingRoot));
    }

    /// <summary>
    /// 🔴🔴 <b><c>S4</c>'s FINDING — terrain does NOT inherit the ARTIFACT half, and cannot.</b>
    ///
    /// <para>📐 Measured: the TKB artifact is <c>{{node}}/TKB/{{name}}.zip</c>
    /// (<c>TkbLoadClusterStateHandler.cs:78</c>) and the terrain definition is
    /// <c>{{node}}/Terrain/{{name}}.json</c> (<c>TerrainLoadClusterStateHandler.cs:138</c>) — a
    /// DIFFERENT DIRECTORY and a DIFFERENT EXTENSION. ⇒ <c>S2b</c>, which copies one named zip out of
    /// <c>{{nas}}/tkb</c>, cannot serve it; staging terrain needs a second artifact KIND.</para>
    ///
    /// <para>⛔ <b>Deliberately NOT built here.</b> The dispatch ruled that a terrain-specific path is a
    /// FINDING to report rather than work to do, and a second artifact kind is squarely the wider
    /// asset-management model the dispatch fenced off. ⭐ This rail PINS the gap so it cannot be
    /// mistaken for done: it asserts the two roots differ, which is the whole reason.</para>
    ///
    /// <para>⇒ <c>BP-550</c> is closed for the TKB and stays OPEN for terrain. 📄 reported in
    /// docs/blueprints/batches/REPORT_Artifact_Staging.md and folded into
    /// docs/DESIGN_Artifact_Staging.md §5a.</para>
    /// </summary>
    [Fact]
    public void TerrainArtifactsAreNotStagedByTheTkbPath_AndTheRootsDiffer()
    {
        var tkbRoot = Fdp.Toolkit.Orchestration.OrchestrationConstants.GetTkbStagingRoot(_stagingRoot);

        Assert.Equal(_tkbDir, tkbRoot);
        Assert.NotEqual(_terrainDir, tkbRoot);

        // ⭐ The header (S2c) reaches the terrain reader; the DEFINITION file does not arrive with it.
        var json = Hrot.Orchestrator.StorageGatewayModule.BuildStagedHeaderJson(
            new Hrot.Orchestrator.StagedArtifactNames(null, "basic-desert"));
        File.WriteAllText(Path.Combine(_tkbDir, "ScenarioHeader.json"), json, new UTF8Encoding(false));

        Assert.Equal("basic-desert", Fdp.Toolkit.Terrain.ScenarioTerrainName.Read(_stagingRoot));
        Assert.False(File.Exists(Path.Combine(_terrainDir, "basic-desert.json")),
            "nothing in the TKB staging path publishes a terrain definition — that is S4's finding");
    }
}
