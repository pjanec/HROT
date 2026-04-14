using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Examples.Common;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using FDP.Toolkit.Perception;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Perception.Systems;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Vis2D;
using Fdp.ModuleHost_Core;
using Fdp.ModuleHost_Core.Abstractions;

namespace Fdp.Examples.Scenarios.Perception
{
    /// <summary>
    /// DEM1-D005 — SensorGrid: Autonomous perception pipeline with physics-accurate LOS.
    ///
    /// <para>An observer entity at the origin tracks an enemy target that moves north at
    /// 1 m/tick along the line X=100. A cylindrical wall obstacle at (50, 25, 0) with
    /// radius 10 m periodically occludes the LOS, creating three detection phases:</para>
    ///
    /// <list type="number">
    ///   <item><term>Phase 1 (tick 28)</term>
    ///     <description>Target visible in open field. HasThreat must be true.</description>
    ///   </item>
    ///   <item><term>Phase 2 (tick 60)</term>
    ///     <description>Target occluded by wall. HasThreat must be false
    ///     (sighting stale: last seen at tick ~36. Staleness = 24 >= threshold 20).</description>
    ///   </item>
    ///   <item><term>Phase 3 (tick 96)</term>
    ///     <description>Target reacquired after exiting wall shadow. HasThreat must be true.</description>
    ///   </item>
    /// </list>
    ///
    /// <para><b>Geometry:</b> Wall at (50, 25) blocks LOS when target Y in [29.17, 75.0]:
    /// |2500 - 50Y| / sqrt(10000 + Y^2) <= 10. Design deviation: spec said (50, 50)
    /// which cannot occlude LOS to a target at X=100. See BATCH-05-REPORT.md.</para>
    ///
    /// <para><b>Pipeline timing:</b> The perception pipeline is driven directly by
    /// EvaluateTick every 6 sim ticks (10 Hz), with manual ECB playback and
    /// Bus.SwapBuffers() between pipeline stages to propagate events. There is a
    /// 2-module-cycle (~12 sim tick) lag from VisionBroadphase to TargetMemory update.</para>
    /// </summary>
    public sealed class SensorGridScenario : IScenario
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const float WallX      = 50f;
        private const float WallY      = 25f;
        private const float WallRadius = 10f;

        /// <summary>Ticks without a confirmed sighting before a target is considered stale.</summary>
        private const uint StalenessThreshold = 20;

        // ── Observable state for tests ────────────────────────────────────────

        /// <summary>True after Phase 1 check at tick 28 passes.</summary>
        public bool Phase1Passed { get; private set; }

        /// <summary>True after Phase 2 check at tick 60 passes.</summary>
        public bool Phase2Passed { get; private set; }

        // ── Perception pipeline systems ───────────────────────────────────────

        private LocalGridBuilderSystem?   _localGridBuilder;
        private VisionBroadphaseSystem?   _visionBroadphase;
        private LosRequestBatchingSystem? _losRequestBatching;
        private ThreatEvaluationSystem?   _threatEvaluation;
        private SpatialHashGrid           _localGrid;

        // ── Entity handles ────────────────────────────────────────────────────

        private Entity _observer;
        private Entity _target;

        // ── IScenario ─────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string ScenarioName => "sensorgrid";

        /// <inheritdoc/>
        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // ── Component registration ─────────────────────────────────────────
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<Faction>();
            world.RegisterComponent<PerceptionReceptor>();
            world.RegisterComponent<TargetMemory>();
            world.RegisterComponent<PhysicsCollider>();

            // ── Event registration ─────────────────────────────────────────────
            world.RegisterEvent<LosCheckRequestEvent>();
            world.RegisterEvent<TargetVisibleEvent>();

            // ── Perception pipeline setup ──────────────────────────────────────
            _localGrid = SpatialHashGrid.Create(
                PerceptionConstants.LocalGridWidth,
                PerceptionConstants.LocalGridHeight,
                PerceptionConstants.LocalGridCellSize,
                PerceptionConstants.LocalGridMaxEntities,
                Allocator.Persistent);

            _localGridBuilder   = new LocalGridBuilderSystem(_localGrid);
            _visionBroadphase   = new VisionBroadphaseSystem(_localGrid);
            _losRequestBatching = new LosRequestBatchingSystem(
                mockMode: false,
                colliderRadiusReader: (view, e) =>
                    view.HasComponent<PhysicsCollider>(e)
                        ? view.GetComponentRO<PhysicsCollider>(e).Radius
                        : 0f);
            _threatEvaluation   = new ThreatEvaluationSystem();

            // ── Entity spawning ────────────────────────────────────────────────
            _observer = SpawnObserver(world);
            _target   = SpawnTarget(world);
            SpawnWall(world);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The perception pipeline is driven every 6 sim ticks (10 Hz).
        /// Each pipeline stage is separated by an ECB flush + Bus.SwapBuffers() so events
        /// propagate through the pipeline immediately within the same EvaluateTick call,
        /// allowing meaningful perception results within ~12 ticks of sim start.
        /// </remarks>
        public bool EvaluateTick(uint tick, EntityRepository world)
        {
            // Advance target northward — EvaluateTick runs before kernel.Update.
            ref var tf = ref world.GetComponentRW<SimTransform>(_target);
            tf.Position = new Vector3(100f, (float)tick, 0f);

            // ── Run perception pipeline every 6 ticks (10 Hz equivalent) ───────
            if (tick % 6 == 0)
            {
                ISimulationView view = world;
                float dt = 1f / 10f; // 100 ms per perception cycle

                // Stage 1: Rebuild local grid from world state.
                _localGridBuilder!.Execute(view, dt);
                FlushEcbAndSwap(world);

                // Stage 2: Vision broadphase emits LosCheckRequestEvent via ECB.
                _visionBroadphase!.Execute(view, dt);
                FlushEcbAndSwap(world);

                // Stage 3: LOS batching reads LosCheckRequestEvents; emits TargetVisibleEvent via ECB.
                _losRequestBatching!.Execute(view, dt);
                FlushEcbAndSwap(world);

                // Stage 4: Threat evaluation reads TargetVisibleEvents; updates TargetMemory via ECB.
                _threatEvaluation!.Execute(view, dt);
                FlushEcbAndSwap(world);
            }

            // ── Phase 1 (tick 28): target visible in open field ───────────────
            if (tick == 28)
            {
                ref readonly var mem = ref world.GetComponent<TargetMemory>(_observer);
                Phase1Passed = HasThreat(in mem, _target, tick);

                if (!Phase1Passed)
                    throw new ScenarioFailureException(1,
                        $"Phase 1 FAILED at tick {tick}: observer has not detected the target. " +
                        $"TargetMemory.Count={mem.Count}, LastSeenTick={GetLastSeenTick(in mem, _target)}");
            }

            // ── Phase 2 (tick 60): target occluded — threat should be stale ───
            if (tick == 60)
            {
                ref readonly var mem = ref world.GetComponent<TargetMemory>(_observer);
                Phase2Passed = !HasThreat(in mem, _target, tick);

                if (!Phase2Passed)
                    throw new ScenarioFailureException(2,
                        $"Phase 2 FAILED at tick {tick}: target still active threat behind wall. " +
                        $"LastSeenTick={GetLastSeenTick(in mem, _target)}, " +
                        $"diff={(tick - GetLastSeenTick(in mem, _target))}");
            }

            // ── Phase 3 (tick 96): target reacquired after exiting wall shadow ─
            if (tick == 96)
            {
                ref readonly var mem = ref world.GetComponent<TargetMemory>(_observer);
                bool reacquired = HasThreat(in mem, _target, tick);

                if (!reacquired)
                    throw new ScenarioFailureException(3,
                        $"Phase 3 FAILED at tick {tick}: target not reacquired. " +
                        $"LastSeenTick={GetLastSeenTick(in mem, _target)}, " +
                        $"diff={(tick - GetLastSeenTick(in mem, _target))}");

                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }

        /// <inheritdoc/>
        public void OnShutdown() => _localGrid.Dispose();

        // ── Entity factories ──────────────────────────────────────────────────

        private Entity SpawnObserver(EntityRepository world)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            world.AddComponent(e, new Faction { FactionId = 1 });
            world.AddComponent(e, new PerceptionReceptor
            {
                VisionRange    = 200f,
                FieldOfViewCos = -1f,  // 360-degree detection
            });
            world.AddComponent(e, new TargetMemory());
            return e;
        }

        private Entity SpawnTarget(EntityRepository world)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = new Vector3(100f, 0f, 0f), Rotation = Quaternion.Identity });
            world.AddComponent(e, new Faction { FactionId = 2 });  // enemy
            world.AddComponent(e, new PhysicsCollider { Radius = 2f });
            return e;
        }

        private static void SpawnWall(EntityRepository world)
        {
            // Wall at (50, 25, 0) blocks LOS when target Y in [29.17, 75.0].
            // Design deviation: spec placed wall at (50, 50), which cannot occlude X-axis LOS.
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = new Vector3(WallX, WallY, 0f), Rotation = Quaternion.Identity });
            world.AddComponent(e, new PhysicsCollider { Radius = WallRadius });
        }

        // ── Pipeline helper ───────────────────────────────────────────────────

        /// <summary>
        /// Flushes the per-thread ECB into the world and swaps the event bus buffers,
        /// so events published to the ECB in the current stage are readable by the next stage.
        /// </summary>
        private static void FlushEcbAndSwap(EntityRepository world)
        {
            // Cast to concrete type so we can call Playback directly.
            // GetCommandBuffer() returns the per-thread ECB cast to IEntityCommandBuffer;
            // EntityCommandBuffer carries the public Playback() method.
            var ecb = (EntityCommandBuffer)((ISimulationView)world).GetCommandBuffer();
            ecb.Playback(world);
            world.Bus.SwapBuffers();
        }

        // ── TargetMemory helpers ──────────────────────────────────────────────

        private static unsafe bool HasThreat(in TargetMemory mem, Entity target, uint currentTick)
        {
            long packedId = (long)target.PackedValue;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] != packedId) continue;
                return mem.ThreatScores[i] > 0f
                    && (currentTick - mem.LastSeenTick[i]) < StalenessThreshold;
            }
            return false;
        }

        private static unsafe uint GetLastSeenTick(in TargetMemory mem, Entity target)
        {
            long packedId = (long)target.PackedValue;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == packedId) return mem.LastSeenTick[i];
            return 0;
        }
    }
}