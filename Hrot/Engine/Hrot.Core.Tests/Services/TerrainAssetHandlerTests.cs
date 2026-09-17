using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.Core.Tests.Services
{
    /// <summary>
    /// D3 — the ONE terrain/zone op handler every ECS host registers.
    ///
    /// <para>⭐⭐ The contract under test is mostly a NEGATIVE one: <b>the ACK is unconditional and there
    /// is no role × kind matrix</b> (§8.3 N6). A host with no loader composed, or no zones, must still
    /// complete the round rather than stall it — that is what let <c>IgZoneDummyHandler</c> be deleted
    /// instead of replaced.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §3.1, §8.3, §9.3.
    /// </summary>
    public sealed class TerrainAssetHandlerTests
    {
        private sealed class RecordingTileLoader : IZoneTileLoader
        {
            public readonly List<ulong> Built = new();
            public bool Result = true;

            public bool Build(Vector2 min, Vector2 max, ulong footprintHash)
            {
                Built.Add(footprintHash);
                return Result;
            }
        }

        private static EntityRepository NewRepo()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<TerrainAssetLoadState>();
            repo.RegisterManagedComponent<EditablePolyline>();
            return repo;
        }

        private static List<Vector2> Triangle() => new()
        {
            new Vector2(-53f, -88.5f), new Vector2(47f, -88.5f), new Vector2(47f, 11.5f),
        };

        private static Entity NewZone(EntityRepository repo, long networkId, Vector3 origin)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new TkbIdentity { TkbType = TkbEntityTypes.TerrainZone });
            repo.AddComponent(e, new NetworkIdentity(networkId));
            repo.AddComponent(e, new SimTransform { Position = origin });
            repo.SetManagedComponent(e, new EditablePolyline { Points = Triangle() });
            return e;
        }

        private static ExecuteNodeOpIntent Op(NodeOpType operation, Guid tx, object? payload = null)
            => new ExecuteNodeOpIntent
            {
                TransactionId = tx,
                TargetNodeId  = 0,
                Operation     = operation,
                DomainPayload = payload,
            };

        /// <summary>Drives one node through the round exactly as <c>ClusterSlave</c> does.</summary>
        private static void RunRound(TerrainAssetHandler handler, Guid tx, string? zoneId)
        {
            var prepare = Op(NodeOpType.PrepareZone, tx,
                zoneId == null ? null : new ZoneOpPayload(zoneId));
            handler.PrepareAsync(prepare, CancellationToken.None).GetAwaiter().GetResult();
            handler.Commit(prepare, repo: null);      // ⚠ the slave commits the prepare intent too

            var commit = Op(NodeOpType.CommitZone, tx,
                zoneId == null ? null : new ZoneOpPayload(zoneId));
            handler.PrepareAsync(commit, CancellationToken.None).GetAwaiter().GetResult();
            handler.Commit(commit, repo: null);
        }

        // ── the unconditional ACK ────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ §8.3 N6 — a host that composed NO terrain loader still completes the round. This is the
        /// whole reason <c>IgZoneDummyHandler</c> could be deleted rather than generalised.
        /// </summary>
        [Fact]
        public void AHostWithNoLoaderComposed_StillAcksTheWholeRound()
        {
            using var repo = NewRepo();
            NewZone(repo, 7, new Vector3(670f, 473.5f, 0f));

            var handler = new TerrainAssetHandler(service: null, world: repo);

            var ex = Record.Exception(() => RunRound(handler, Guid.NewGuid(), "7"));

            Assert.Null(ex);   // a throw would be a Failure ACK and would stall the cluster
        }

        /// <summary>A host with a loader but no zones has nothing to do, and says so by succeeding.</summary>
        [Fact]
        public void AHostWithNoZones_AcksAndStampsNothing()
        {
            using var repo = NewRepo();
            var tiles   = new RecordingTileLoader();
            var handler = new TerrainAssetHandler(new TerrainLoadService(tiles), repo);

            RunRound(handler, Guid.NewGuid(), zoneId: null);

            Assert.Empty(tiles.Built);
        }

        [Fact]
        public void ItClaimsBothOpPairs_AndTheAbort_AndNothingElse()
        {
            var handler = new TerrainAssetHandler(service: null, world: null);

            Assert.True(handler.CanHandle(NodeOpType.PrepareZone));
            Assert.True(handler.CanHandle(NodeOpType.CommitZone));
            Assert.True(handler.CanHandle(NodeOpType.PrepareTerrainAsset));
            Assert.True(handler.CanHandle(NodeOpType.CommitTerrainAsset));

            // 🔴 Claimed because measured 2026-09-17: NOTHING in the tree claimed AbortTransaction, so the
            //    master's abort fan-out auto-ACKed Success while no node rolled anything back.
            Assert.True(handler.CanHandle(NodeOpType.AbortTransaction));

            Assert.False(handler.CanHandle(NodeOpType.PrepareLive));
            Assert.False(handler.CanHandle(NodeOpType.SerializeLocal));
        }

        // ── the happy path, and the phase split ──────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ The phase split is the load-bearing part: the tiles are built in PREPARE (where the time
        /// goes, and where ECS may not be touched) and the marker lands in COMMIT (main thread, after the
        /// cluster barrier). ⛔ A marker stamped at prepare would record residency on this node while
        /// another node's prepare was still failing.
        /// </summary>
        [Fact]
        public void PrepareBuildsButStampsNothing_CommitStamps()
        {
            using var repo = NewRepo();
            var origin  = new Vector3(670f, 473.5f, 0f);
            var zone    = NewZone(repo, 7, origin);
            var tiles   = new RecordingTileLoader();
            var handler = new TerrainAssetHandler(new TerrainLoadService(tiles), repo);
            var tx      = Guid.NewGuid();

            var prepare = Op(NodeOpType.PrepareZone, tx, new ZoneOpPayload("7"));
            handler.PrepareAsync(prepare, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Single(tiles.Built);
            Assert.False(repo.HasComponent<TerrainAssetLoadState>(zone));

            handler.Commit(prepare, repo: null);
            Assert.False(repo.HasComponent<TerrainAssetLoadState>(zone));   // still the prepare phase

            var commit = Op(NodeOpType.CommitZone, tx, new ZoneOpPayload("7"));
            handler.PrepareAsync(commit, CancellationToken.None).GetAwaiter().GetResult();
            handler.Commit(commit, repo: null);

            Assert.True(repo.HasComponent<TerrainAssetLoadState>(zone));
            var marker = repo.GetComponent<TerrainAssetLoadState>(zone);
            Assert.Equal(LoadPhase.Loaded, marker.Phase);
            Assert.Equal(ZoneFootprint.Compute(origin, Triangle()), marker.SourceHash);
        }

        /// <summary>
        /// ⭐ §9.3 — ONE op per zone. A round naming zone A must not touch zone B, even though both are
        /// stale, or "retry just this zone" would silently mean "retry everything".
        /// </summary>
        [Fact]
        public void ARoundNamingOneZone_LeavesTheOtherZoneAlone()
        {
            using var repo = NewRepo();
            var zoneA = NewZone(repo, 7,  new Vector3(670f, 473.5f, 0f));
            var zoneB = NewZone(repo, 42, new Vector3(-120f, 40f, 0f));
            var tiles = new RecordingTileLoader();
            var handler = new TerrainAssetHandler(new TerrainLoadService(tiles), repo);

            RunRound(handler, Guid.NewGuid(), zoneId: "7");

            Assert.Single(tiles.Built);
            Assert.True(repo.HasComponent<TerrainAssetLoadState>(zoneA));
            Assert.False(repo.HasComponent<TerrainAssetLoadState>(zoneB));
        }

        /// <summary>
        /// The asset-build pair carries no zone filter, so it sweeps every stale zone.
        /// </summary>
        [Fact]
        public void TheAssetBuildPair_SweepsEveryZone()
        {
            using var repo = NewRepo();
            NewZone(repo, 7,  new Vector3(670f, 473.5f, 0f));
            NewZone(repo, 42, new Vector3(-120f, 40f, 0f));
            var tiles   = new RecordingTileLoader();
            var handler = new TerrainAssetHandler(new TerrainLoadService(tiles), repo);
            var tx      = Guid.NewGuid();

            var prepare = Op(NodeOpType.PrepareTerrainAsset, tx, new TerrainAssetOpPayload(null));
            handler.PrepareAsync(prepare, CancellationToken.None).GetAwaiter().GetResult();
            handler.Commit(prepare, repo: null);

            var commit = Op(NodeOpType.CommitTerrainAsset, tx, new TerrainAssetOpPayload(null));
            handler.PrepareAsync(commit, CancellationToken.None).GetAwaiter().GetResult();
            handler.Commit(commit, repo: null);

            Assert.Equal(2, tiles.Built.Count);
        }

        // ── failure and rollback ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⛔ §8.3 N3 — there are TWO outcomes, satisfied and failed, and a host that cannot make the
        /// coverage resident FAILS LOUDLY. A faulted prepare is what <c>ClusterSlave</c> turns into a
        /// Failure ACK, which is what makes the master abort the round.
        /// </summary>
        [Fact]
        public void AZoneThatCannotBecomeResident_FailsThePrepareLoudly()
        {
            using var repo = NewRepo();
            NewZone(repo, 7, new Vector3(670f, 473.5f, 0f));
            var tiles   = new RecordingTileLoader { Result = false };
            var handler = new TerrainAssetHandler(new TerrainLoadService(tiles), repo);

            var prepare = Op(NodeOpType.PrepareZone, Guid.NewGuid(), new ZoneOpPayload("7"));

            Assert.Throws<InvalidOperationException>(() =>
                handler.PrepareAsync(prepare, CancellationToken.None).GetAwaiter().GetResult());
        }

        /// <summary>
        /// ⭐ The abort arrives as an <c>AbortTransaction</c> NodeOp naming the round. The zone that
        /// genuinely failed on THIS node is recorded <c>Failed</c>, so an operator can see which one
        /// broke instead of reading "never loaded".
        /// </summary>
        [Fact]
        public void AnAbortNamingTheRound_RecordsTheZoneThatFailed()
        {
            using var repo = NewRepo();
            var origin = new Vector3(670f, 473.5f, 0f);
            var zone   = NewZone(repo, 7, origin);
            var tiles  = new RecordingTileLoader { Result = false };
            var handler = new TerrainAssetHandler(new TerrainLoadService(tiles), repo);
            var tx = Guid.NewGuid();

            Assert.Throws<InvalidOperationException>(() =>
                handler.PrepareAsync(Op(NodeOpType.PrepareZone, tx, new ZoneOpPayload("7")),
                                     CancellationToken.None).GetAwaiter().GetResult());

            var abort = Op(NodeOpType.AbortTransaction, Guid.NewGuid(), new AbortTransactionPayload(tx));
            handler.PrepareAsync(abort, CancellationToken.None).GetAwaiter().GetResult();
            handler.Commit(abort, repo: null);

            Assert.True(repo.HasComponent<TerrainAssetLoadState>(zone));
            Assert.Equal(LoadPhase.Failed, repo.GetComponent<TerrainAssetLoadState>(zone).Phase);
        }

        /// <summary>
        /// ⚠ The other half of the abort contract: a node whose prepare SUCCEEDED is aborted too, and its
        /// marker must be left exactly as it was — its tiles really are resident, it simply must not
        /// record a commit that never happened.
        /// </summary>
        [Fact]
        public void AnAbortOnANodeThatSucceeded_LeavesItsMarkerUntouched()
        {
            using var repo = NewRepo();
            var zone    = NewZone(repo, 7, new Vector3(670f, 473.5f, 0f));
            var tiles   = new RecordingTileLoader();
            var handler = new TerrainAssetHandler(new TerrainLoadService(tiles), repo);
            var tx      = Guid.NewGuid();

            var prepare = Op(NodeOpType.PrepareZone, tx, new ZoneOpPayload("7"));
            handler.PrepareAsync(prepare, CancellationToken.None).GetAwaiter().GetResult();
            handler.Commit(prepare, repo: null);

            var abort = Op(NodeOpType.AbortTransaction, Guid.NewGuid(), new AbortTransactionPayload(tx));
            handler.PrepareAsync(abort, CancellationToken.None).GetAwaiter().GetResult();
            handler.Commit(abort, repo: null);

            Assert.False(repo.HasComponent<TerrainAssetLoadState>(zone));
        }

        // ── D4: the registrar is what replaced the bespoke dummy handler ─────────────────────

        /// <summary>
        /// ⭐⭐⭐ D4 — <c>IgZoneDummyHandler</c> is deleted, and this is the claim that has to survive it:
        /// a host that composes NOTHING still registers a handler that CLAIMS the zone ops, so the round
        /// is answered rather than falling through to the slave's no-handler path.
        ///
        /// <para>⚠ Why claiming matters even though the slave auto-ACKs an unclaimed op: that fallback
        /// ACKs with <c>IsParticipating: false</c> and gives no host a place to put the work when it DOES
        /// compose a loader. The dummy handler was redundant with that fallback; the registrar is not —
        /// it is the ONE seam, occupied on every host.</para>
        /// </summary>
        [Fact]
        public void TheRegistrar_GivesEvenAnEmptyHostAHandlerThatClaimsTheZoneOps()
        {
            using var slave = new ClusterSlave(new FdpEventBus(), nodeId: 300);

            TerrainAssetRegistrar.Register(slave, service: null, world: null, nodeId: 300);

            Assert.True(slave.IsHandlerRegistered<TerrainAssetHandler>());
        }

        // ── D5: the terrain-identity check ───────────────────────────────────────────────────

        private static string NewStagingRootNaming(string? terrainName)
        {
            string root = Path.Combine(Path.GetTempPath(), "TerrainIdentity_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(root, "TKB"));
            string body = terrainName == null
                ? "{\"SubsystemType\":\"Test\"}"
                : $"{{\"SubsystemType\":\"Test\",\"TerrainName\":\"{terrainName}\"}}";
            File.WriteAllText(Path.Combine(root, "TKB", "ScenarioHeader.json"), body);
            return root;
        }

        /// <summary>
        /// ⛔⛔ D5 / §8.3 N4 — loading the terrain a scenario names is MANDATORY, so a node that does not
        /// hold it FAILS LOUDLY and NAMES ITSELF. ⚠ This is the case the check exists for: a host that
        /// composed no terrain loader at all, which until now passed every op silently.
        /// </summary>
        [Fact]
        public void ANodeThatDoesNotHoldTheScenariosTerrain_FailsLoudlyAndNamesItself()
        {
            string root = NewStagingRootNaming("kandahar");
            try
            {
                using var repo = NewRepo();
                NewZone(repo, 7, new Vector3(670f, 473.5f, 0f));
                var handler = new TerrainAssetHandler(service: null, world: repo, nodeId: 77,
                                                      localStagingRoot: root);

                var ex = Assert.Throws<InvalidOperationException>(() =>
                    handler.PrepareAsync(Op(NodeOpType.PrepareZone, Guid.NewGuid(), new ZoneOpPayload("7")),
                                         CancellationToken.None).GetAwaiter().GetResult());

                Assert.Contains("77", ex.Message);           // WHICH node
                Assert.Contains("kandahar", ex.Message);     // WHICH terrain
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        /// <summary>
        /// ⭐ A node that DOES hold the named terrain passes — the check must not be a blanket refusal.
        /// </summary>
        [Fact]
        public void ANodeHoldingTheNamedTerrain_Passes()
        {
            string root = NewStagingRootNaming("kandahar");
            try
            {
                using var repo = NewRepo();
                repo.RegisterManagedComponent<TerrainDefinition>();
                repo.SetSingletonManaged(new TerrainDefinition { Name = "kandahar" });
                var tiles   = new RecordingTileLoader();
                var handler = new TerrainAssetHandler(new TerrainLoadService(tiles), repo, 77, root);

                var ex = Record.Exception(() => RunRound(handler, Guid.NewGuid(), zoneId: null));

                Assert.Null(ex);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        /// <summary>
        /// ⭐ A scenario that names NO terrain is legal (§2.1e ①) and must not trip the check — otherwise
        /// "mandatory when named" would silently become "mandatory always".
        /// </summary>
        [Fact]
        public void AScenarioNamingNoTerrain_DoesNotTripTheCheck()
        {
            string root = NewStagingRootNaming(terrainName: null);
            try
            {
                using var repo = NewRepo();
                var handler = new TerrainAssetHandler(service: null, world: repo, nodeId: 77,
                                                      localStagingRoot: root);

                var ex = Record.Exception(() => RunRound(handler, Guid.NewGuid(), zoneId: null));

                Assert.Null(ex);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        // ── the id ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠ DEVIATION pinned: a zone's cluster-wide id is its <c>NetworkIdentity.Value</c> as a string,
        /// because no name component exists on a zone entity (<c>TkbIdentity</c> carries only
        /// <c>TkbType</c>). §9.5 draws a "zone name" column; this is what is actually available.
        /// </summary>
        [Fact]
        public void TheZoneIdIsTheNetworkIdentity_RenderedInvariantly()
        {
            using var repo = NewRepo();
            var zone = NewZone(repo, 4242, new Vector3(0f, 0f, 0f));

            Assert.Equal("4242", TerrainLoadService.ZoneIdOf(repo, zone));
            Assert.Equal(4242L.ToString(CultureInfo.InvariantCulture),
                         TerrainLoadService.ZoneIdOf(repo, zone));
        }
    }
}
