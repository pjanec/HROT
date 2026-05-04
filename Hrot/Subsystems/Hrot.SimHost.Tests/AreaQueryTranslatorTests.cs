using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.IG.Components;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;
using Hrot.Network.NED.SimHost;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the EQS area-query network translators
    /// (TASK-HA004: SC-HA004-1, SC-HA004-2, SC-HA004-3).
    ///
    /// Uses stub DDS reader/writer adapters so no live DDS participant is needed.
    /// Two in-process EntityRepository instances model the Brain node and the Muscle node.
    /// </summary>
    public class AreaQueryTranslatorTests : IDisposable
    {
        // ── Stub writer captures all published DDS samples ────────────────────────

        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(repo);
            return repo;
        }

        private static void DisposeEqsSingletons(EntityRepository world)
        {
            if (world.HasSingleton<AreaQueryBatchData>())
            {
                ref var b = ref world.GetSingleton<AreaQueryBatchData>();
                if (b.Requests.IsCreated) b.Requests.Dispose();
                if (b.Results.IsCreated)  b.Results.Dispose();
            }
            if (world.HasSingleton<EqsTargetPool>())
            {
                var p = world.GetSingleton<EqsTargetPool>();
                if (p.Targets.IsCreated) p.Targets.Dispose();
            }
        }

        private static SpatialHashGrid _sharedGrid;
        private static bool _gridCreated;

        private static EntityRepository CreateMuscleWorld(out SpatialHashGrid grid)
        {
            var repo = CreateWorld();
            grid = SpatialHashGrid.Create(300, 300, 50f, 1000, Allocator.Persistent);
            grid.Clear();
            repo.SetSingleton(new SpatialGridData { Grid = grid });
            return repo;
        }

        private static Entity AddArea(EntityRepository repo, List<Vector2> polygon)
        {
            var entity = repo.CreateEntity();
            var ecb = (Fdp.Core.EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
            ecb.AddManagedComponent(entity, new EditablePolyline
            {
                Points  = new List<Vector2>(polygon),
                Version = 1,
            });
            ecb.Playback(repo);
            return entity;
        }

        private static Entity AddEnemy(EntityRepository repo, float x, float y, SpatialHashGrid grid)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new SimTransform
            {
                Position = new Vector3(x, y, 0f),
                Rotation = Quaternion.Identity,
            });
            repo.AddComponent(e, new EntityInfo { ForceId = ForceId.Hostile });
            grid.Add(e, new Vector2(x, y));
            repo.SetSingleton(new SpatialGridData { Grid = grid });
            return e;
        }

        // ── Shared world instances (disposed in Dispose) ──────────────────────────

        private readonly EntityRepository _brainRepo;
        private readonly EntityRepository _muscleRepo;
        private readonly SpatialHashGrid  _muscleGrid;

        public AreaQueryTranslatorTests()
        {
            _brainRepo  = CreateWorld();
            _muscleRepo = CreateMuscleWorld(out _muscleGrid);
        }

        public void Dispose()
        {
            DisposeEqsSingletons(_brainRepo);
            DisposeEqsSingletons(_muscleRepo);
            _muscleGrid.Dispose();
            _brainRepo.Dispose();
            _muscleRepo.Dispose();
        }

        // ── SC-HA004-1: End-to-end pipeline (single process, stub DDS) ───────────

        /// <summary>
        /// Full round-trip test: Brain egress -> Muscle ingress -> AreaQuerySolverSystem
        /// -> Muscle egress -> Brain ingress.  Two enemy entities inside the polygon must
        /// be resolved back to the Brain with TargetCount == 2.
        /// </summary>
        [Fact]
        public void SC_HA004_1_AreaQueryPipeline_BrainRequestReachesBack_WithTargets()
        {
            const long areaNetworkId  = 5000L;
            const long enemy1NetworkId = 6001L;
            const long enemy2NetworkId = 6002L;
            const int  brainNodeId    = 1;

            // Brain and Muscle each have their own entity map (different worlds, different entities).
            var brainEntityMap  = new NetworkEntityMap();
            var muscleEntityMap = new NetworkEntityMap();

            // ── Muscle-side setup ─────────────────────────────────────────────────

            // Area polygon: (-10,-50) -> (100,-50) -> (100,10) -> (-10,10)
            var polygon = new List<Vector2>
            {
                new(-10f, -50f), new(100f, -50f),
                new(100f,  10f), new(-10f,  10f),
            };
            var muscleArea = AddArea(_muscleRepo, polygon);
            muscleEntityMap.Register(areaNetworkId, muscleArea);

            // Two enemies inside the polygon.
            var muscleEnemy1 = AddEnemy(_muscleRepo, 20f, -20f, _muscleGrid);
            var muscleEnemy2 = AddEnemy(_muscleRepo, 70f, -20f, _muscleGrid);
            muscleEntityMap.Register(enemy1NetworkId, muscleEnemy1);
            muscleEntityMap.Register(enemy2NetworkId, muscleEnemy2);

            // ── Brain-side setup ──────────────────────────────────────────────────

            // Area entity on the Brain side (different entity, same network ID).
            var brainAreaEntity = _brainRepo.CreateEntity();
            brainEntityMap.Register(areaNetworkId, brainAreaEntity);

            // Commander entity submits the area query.
            var commander = _brainRepo.CreateEntity();
            long requestId = AreaQueryBatchHelper.RequestAreaQuery(
                _brainRepo, commander, brainAreaEntity, ForceId.Hostile, sourceNodeId: brainNodeId);
            Assert.True(requestId >= 0, "RequestAreaQuery must succeed");

            // Manually stamp SourceNodeId so the Brain egress authority check passes.
            ref var batch = ref _brainRepo.GetSingleton<AreaQueryBatchData>();
            ref var req   = ref batch.Requests[0];
            req.SourceNodeId = brainNodeId;

            // ── Step 1: Brain egress publishes to stub writer ─────────────────────
            var brainEgressWriter  = new CapturingWriter<AreaQueryRequestBatch>();
            var brainEgressTrans   = new AreaQueryBrainEgressTranslator(brainEgressWriter, brainEntityMap, brainNodeId);

            brainEgressTrans.ScanAndPublish(_brainRepo);

            Assert.Equal(1, brainEgressWriter.Written.Count);
            Assert.Equal(1, brainEgressTrans.SentSampleCount);
            var publishedRequest = brainEgressWriter.Written[0];
            Assert.Equal(1, publishedRequest.Requests?.Count ?? 0);
            Assert.Equal(1, brainEgressTrans.SentSampleCount);

            // ── Step 2: Muscle ingress receives the request ───────────────────────
            var muscleIngressTrans = new AreaQueryMuscleIngressTranslator(null, muscleEntityMap);
            muscleIngressTrans.ProcessBatch(publishedRequest, _muscleRepo);

            ref var muscleBatch = ref _muscleRepo.GetSingleton<AreaQueryBatchData>();
            Assert.Equal(1, muscleBatch.Count);

            // ── Step 3: AreaQuerySolverSystem resolves the request ────────────────
            var solver = new AreaQuerySolverSystem();
            solver.Execute(_muscleRepo, 0.016f);

            ref var muscleBatchAfterSolve = ref _muscleRepo.GetSingleton<AreaQueryBatchData>();
            Assert.True(muscleBatchAfterSolve.Results[0].IsReady, "Solver must mark result IsReady");
            Assert.Equal(2, muscleBatchAfterSolve.Results[0].TargetCount);

            // ── Step 4: Muscle egress publishes response ──────────────────────────
            var muscleEgressWriter = new CapturingWriter<AreaQueryResponseBatch>();
            var muscleEgressTrans  = new AreaQueryMuscleEgressTranslator(muscleEgressWriter, muscleEntityMap);

            muscleEgressTrans.ScanAndPublish(_muscleRepo);

            Assert.True(muscleEgressWriter.Written.Count > 0, "Muscle egress must publish a response");
            var publishedResponse = muscleEgressWriter.Written[0];
            Assert.NotNull(publishedResponse.Responses);
            Assert.Equal(1, publishedResponse.Responses!.Count);
            Assert.Equal(2, publishedResponse.Responses[0].TargetCount);
            Assert.Equal(1, muscleEgressTrans.SentSampleCount);

            // ── Step 5: Brain ingress receives the response ───────────────────────
            // Register brain-side enemy entities before the ingress processes them.
            var brainEnemy1 = _brainRepo.CreateEntity();
            var brainEnemy2 = _brainRepo.CreateEntity();
            brainEntityMap.Register(enemy1NetworkId, brainEnemy1);
            brainEntityMap.Register(enemy2NetworkId, brainEnemy2);

            var brainIngressTrans = new AreaQueryBrainIngressTranslator(null, brainEntityMap, brainNodeId);

            // Re-set AreaQueryBatchData on Brain so there is a valid request slot.
            // BrainEgress cleared batch.Count; restore it for ingress correlation.
            ref var brainBatch = ref _brainRepo.GetSingleton<AreaQueryBatchData>();
            brainBatch.Requests[0] = new AreaQueryRequest
            {
                RequestId        = requestId,
                TargetAreaEntity = brainAreaEntity,
                TargetForce      = ForceId.Hostile,
                SourceNodeId     = brainNodeId,
            };
            brainBatch.Results[0] = new AreaQueryResult
            {
                RequestId   = requestId,
                IsReady     = false,
                TargetCount = 0,
                TargetGroupHandle = -1,
            };
            brainBatch.Count = 1;

            // Target batch addressed to this Brain node.
            var targetedResponse = new AreaQueryResponseBatch
            {
                TargetNodeId = brainNodeId,
                Responses    = publishedResponse.Responses,
            };
            brainIngressTrans.ProcessBatch(targetedResponse, _brainRepo);

            ref var brainFinalBatch = ref _brainRepo.GetSingleton<AreaQueryBatchData>();
            int slot = (int)(requestId & 0xFFFF_FFFF);
            Assert.True(brainFinalBatch.Results[slot].IsReady, "Brain ingress must mark result IsReady");
            Assert.Equal(2, brainFinalBatch.Results[slot].TargetCount);
        }

        // ── SC-HA004-2: Unresolved area entity on Muscle -> TargetCount == 0 ─────

        /// <summary>
        /// When the Muscle cannot resolve the area entity network ID, the MuscleIngress
        /// translator must write an immediate ready result with TargetCount == 0 and
        /// must not throw an exception.
        /// </summary>
        [Fact]
        public void SC_HA004_2_MuscleIngress_UnresolvedAreaEntity_WritesZeroTargetResponse()
        {
            const long unknownAreaNetworkId = 9999L;
            const int  brainNodeId         = 1;

            var entityMap = new NetworkEntityMap();
            // unknownAreaNetworkId is deliberately NOT registered in entityMap.

            var muscleIngress = new AreaQueryMuscleIngressTranslator(null, entityMap);

            var batch = new AreaQueryRequestBatch
            {
                SourceNodeId = brainNodeId,
                Requests = new List<DdsAreaQueryRequest>
                {
                    new DdsAreaQueryRequest
                    {
                        RequestId           = 42L,
                        TargetAreaNetworkId = unknownAreaNetworkId,
                        SourceNodeId        = brainNodeId,
                        ForceId             = (int)ForceId.Hostile,
                    },
                },
            };

            // ProcessBatch must not throw.
            var exception = Record.Exception(() => muscleIngress.ProcessBatch(batch, _muscleRepo));
            Assert.Null(exception);

            // The Muscle batch must contain 1 entry with IsReady=true and TargetCount=0.
            ref var muscleBatch = ref _muscleRepo.GetSingleton<AreaQueryBatchData>();
            Assert.Equal(1, muscleBatch.Count);
            Assert.True(muscleBatch.Results[0].IsReady, "Result must be immediately ready when area entity is unresolved");
            Assert.Equal(0, muscleBatch.Results[0].TargetCount);
            Assert.Equal(42L, muscleBatch.Results[0].RequestId);
        }

        // ── SC-HA004-3: Unresolved target entity silently skipped ─────────────────

        /// <summary>
        /// When one of the 3 solver-resolved target entities is NOT in the Muscle's
        /// NetworkEntityMap (entity died between solve and egress), the MuscleEgress
        /// translator must publish a response with TargetCount == 2 and must not throw.
        /// </summary>
        [Fact]
        public void SC_HA004_3_MuscleEgress_UnresolvedTargetEntity_SkippedInResponse()
        {
            const long netId1 = 7001L;
            const long netId2 = 7002L;
            // netId3 intentionally NOT registered (simulates death).

            var entityMap = new NetworkEntityMap();

            // Two resolvable entities.
            var e1 = _muscleRepo.CreateEntity();
            var e2 = _muscleRepo.CreateEntity();
            var e3 = _muscleRepo.CreateEntity();  // registered in pool but NOT in map
            entityMap.Register(netId1, e1);
            entityMap.Register(netId2, e2);
            // e3 is not registered.

            // Manually populate EqsTargetPool with 3 entries: e1, e2, e3.
            ref var pool = ref _muscleRepo.GetSingleton<EqsTargetPool>();
            pool.Targets[0] = (long)e1.PackedValue;
            pool.Targets[1] = (long)e2.PackedValue;
            pool.Targets[2] = (long)e3.PackedValue;
            pool.NextFreeIndex = 3;
            _muscleRepo.SetSingleton(pool);

            // Populate AreaQueryBatchData with 1 solved result spanning 3 targets.
            ref var batch = ref _muscleRepo.GetSingleton<AreaQueryBatchData>();
            batch.Requests[0] = new AreaQueryRequest
            {
                RequestId    = 100L,
                TargetForce  = ForceId.Hostile,
                SourceNodeId = 1,
            };
            batch.Results[0] = new AreaQueryResult
            {
                RequestId         = 100L,
                IsReady           = true,
                TargetCount       = 3,
                TargetGroupHandle = 0,    // starts at pool index 0
                SourceNodeId      = 1,
            };
            batch.Count = 1;

            // Run MuscleEgressTranslator.
            var egressWriter = new CapturingWriter<AreaQueryResponseBatch>();
            var egress       = new AreaQueryMuscleEgressTranslator(egressWriter, entityMap);

            var exception = Record.Exception(() => egress.ScanAndPublish(_muscleRepo));
            Assert.Null(exception);

            // Exactly 1 batch published.
            Assert.Equal(1, egressWriter.Written.Count);
            var response = egressWriter.Written[0];
            Assert.NotNull(response.Responses);
            Assert.Equal(1, response.Responses!.Count);

            // Only 2 network IDs could be resolved (e3 not in map).
            Assert.Equal(2, response.Responses[0].TargetCount);
            Assert.Equal(2, response.Responses[0].TargetNetworkIds?.Count ?? -1);

            Assert.Equal(1, egress.SentSampleCount);
        }
    }
}
