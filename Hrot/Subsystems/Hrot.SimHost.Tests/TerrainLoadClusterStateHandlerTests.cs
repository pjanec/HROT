using System;
using System.IO;
using System.Text;
using System.Threading;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Core.Orchestration;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Hrot.SimHost.Orchestration.Handlers;
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
public sealed class TerrainLoadClusterStateHandlerTests : IDisposable
{
    private readonly string _stagingRoot;
    private readonly string _tkbDir;
    private readonly string _terrainDir;

    public TerrainLoadClusterStateHandlerTests()
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

    private static ExecuteNodeOpIntent Intent() => new()
    {
        TransactionId = Guid.NewGuid(),
        TargetNodeId  = 1,
        Operation     = NodeOpType.PrepareLive,
    };

    /// <summary>
    /// Runs PrepareAsync to completion. ⚠ Deliberately a helper rather than an inline
    /// <c>.GetAwaiter().GetResult()</c> in each test: the handler's prepare is pure file I/O with no
    /// synchronization context, so blocking is safe, and keeping it in one place stops the pattern from
    /// being copied into a test that later gains a real async dependency.
    /// </summary>
    private static void Prepare(TerrainLoadClusterStateHandler handler, ExecuteNodeOpIntent intent)
        => handler.PrepareAsync(intent, CancellationToken.None).GetAwaiter().GetResult();

    private (TerrainLoadClusterStateHandler Handler, RoadNetworkHolder Holder) NewHandler()
    {
        var holder = new RoadNetworkHolder();
        return (new TerrainLoadClusterStateHandler(_stagingRoot, holder), holder);
    }

    /// <summary>
    /// A handler wired the way PRODUCTION wires it — the world INJECTED, not handed in at commit.
    /// See <see cref="ItPublishesThroughTheINJECTEDWorld_BecauseClusterSlaveAlwaysCommitsWithNullRepo"/>.
    /// </summary>
    private (TerrainLoadClusterStateHandler Handler, RoadNetworkHolder Holder) NewHandlerWithWorld(
        EntityRepository world)
    {
        var holder = new RoadNetworkHolder();
        return (new TerrainLoadClusterStateHandler(_stagingRoot, holder, world), holder);
    }

    // ── it claims the right ops ───────────────────────────────────────────────────────────────

    [Fact]
    public void ItInterceptsPrepareLiveAndPrepareEdit_AndNothingElse()
    {
        var (handler, holder) = NewHandler();
        using (holder)
        {
            Assert.True(handler.CanHandle(NodeOpType.PrepareLive));
            Assert.True(handler.CanHandle(NodeOpType.PrepareEdit));
            Assert.False(handler.CanHandle(NodeOpType.SerializeLocal));
            Assert.False(handler.CanHandle(NodeOpType.PrepareTerrainAsset));
        }
    }

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
            handler.Abort(aborted, world);

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
    public void ANodeWithNoAuthoringDeps_StillGetsTheTerrainLoader()
    {
        // ⛔⛔ NodeBootstrapper registers the scenario LOAD handlers inside a conditional that needs the
        //    full authoring deps (extractor / source / id-allocator). A pure MuscleGround node passes
        //    NONE of them — and the muscle is precisely the role that consumes the road network.
        //    Hanging terrain off those handlers would leave it unloaded on the node that needs it most.
        var bootstrapper = new NodeBootstrapper();
        using var world  = new EntityRepository();
        using var kernel = new Fdp.ModuleHost.ModuleHostKernel(world, new EventAccumulator());

        using var slave = bootstrapper.BuildOrchestration(
            NodeRole.MuscleGround, kernel, world, nodeId: 1,
            participant: null, eventBus: new FdpEventBus());

        Assert.True(slave.IsHandlerRegistered<TerrainLoadClusterStateHandler>(),
            "a node with NO authoring deps must still load the terrain its scenario names — the muscle "
          + "is the role that consumes the road network");
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
    public void ItPublishesThroughTheINJECTEDWorld_BecauseClusterSlaveAlwaysCommitsWithNullRepo()
    {
        WriteScenarioHeader("kandahar");
        WriteRoadNetwork("roads.json");
        WriteTerrainDefinition("kandahar", "roads.json");

        using var world = NewWorld();
        var (handler, holder) = NewHandlerWithWorld(world);
        using (holder)
        {
            var intent = Intent();
            Prepare(handler, intent);

            // ⭐ Exactly what ClusterSlave does — no repository argument.
            handler.Commit(intent, repo: null);

            Assert.True(world.HasSingleton<ZoneEnvironmentData>(),
                "ClusterSlave commits with repo: null, so a handler that publishes only through that "
              + "parameter publishes nothing at all on every host");
            Assert.Equal("kandahar", world.GetSingletonManaged<TerrainDefinition>()!.Name);
            Assert.True(world.GetSingleton<ZoneEnvironmentData>().RoadNetwork.Nodes.IsCreated);
        }
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
            handler.Commit(intent, repo: null);   // must not throw

            // ⭐ Still only the holder's initial generation — nothing was published, and the staged blob
            //   was freed rather than leaked. (LiveGenerations is `1 + retired-but-leased`, so 1 is the
            //   floor, not zero.)
            Assert.Equal(1, holder.LiveGenerations);
        }
    }
}
