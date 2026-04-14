using System;
using System.Numerics;
using Fdp.Examples.Common;
using Fdp.Examples.Common.Constants;
using Fdp.Examples.Common.Helpers;
using Fdp.Examples.Common.Systems;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Components;
using Fdp.Modules.Geographic.Systems;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Examples.Scenarios.Perception
{
    /// <summary>
    /// DEM1-D007 — TerrainClamping: prove async terrain batching, Z-height smoothing,
    /// and jump-rejection via <c>FDP.Toolkit.Geographic</c> pipeline systems.
    ///
    /// <para><b>Topology (pipeline driven manually in EvaluateTick):</b></para>
    /// <list type="number">
    ///   <item><see cref="TerrainQueryInitializationSystem"/> — reset batch count.</item>
    ///   <item><see cref="TerrainQuerySubmitSystem"/> — submit queries for clamped entities.</item>
    ///   <item><see cref="TerrainQuerySolverSystem"/> — invoke <see cref="MockTerrainProvider"/>.</item>
    ///   <item><see cref="TerrainQueryResolutionSystem"/> — apply hits; jump-rejection.</item>
    ///   <item><see cref="TransformSyncSystem"/> — lerp <c>CurrentZOffset</c> toward <c>TargetZOffset</c>.</item>
    /// </list>
    ///
    /// <para><b>MockTerrainProvider height profile:</b></para>
    /// <list type="bullet">
    ///   <item>0–20 m: Z = 0 (flat ground)</item>
    ///   <item>20–80 m: Z = (x − 20) × 0.2 (ramp, slope 0.2)</item>
    ///   <item>x ≈ 40 m (±0.5 m): Z = 100 (spike / bad-raycast anomaly)</item>
    /// </list>
    ///
    /// <para><b>Phase table:</b></para>
    /// <list type="table">
    ///   <item><term>Phase 1 (tick 10)</term><description>X ≈ 1.7 m — flat zone; CurrentZOffset &lt; 0.01.</description></item>
    ///   <item><term>Phase 2 (tick 150)</term><description>X ≈ 25 m — ramp; TargetZOffset &gt; 0.5 AND CurrentZOffset &lt; TargetZOffset.</description></item>
    ///   <item><term>Phase 3 (tick 240)</term><description>X ≈ 40 m — spike rejected; LastValidIgAltitude &lt; 10.</description></item>
    ///   <item><term>Phase 4 (tick 300)</term><description>X ≈ 50 m — recovery; TargetZOffset ≈ 6.0 (±1.0) → success.</description></item>
    /// </list>
    /// </summary>
    public sealed class TerrainClampingScenario : IScenario
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const float VehicleSpeedMs    = 10f;         // m/s along X axis
        private const float FixedDt           = 1f / 60f;    // 60 Hz deterministic step
        private const float PositionAdvanceM  = VehicleSpeedMs * FixedDt; // ≈ 0.167 m/tick
        private const float SmoothedOffsetTolerance = 1.0f;  // ±1.0 m for Phase 4 assertion

        // ── Observable state for test assertions ──────────────────────────────

        /// <summary>CurrentZOffset captured at tick 10 (Phase 1).</summary>
        public float Phase1CurrentZOffset { get; private set; }

        /// <summary>TargetZOffset captured at tick 150 (Phase 2).</summary>
        public float Phase2TargetZOffset { get; private set; }

        /// <summary>CurrentZOffset captured at tick 150 (Phase 2).</summary>
        public float Phase2CurrentZOffset { get; private set; }

        /// <summary>LastValidIgAltitude captured at tick 240 (Phase 3).</summary>
        public float Phase3LastValidIgAltitude { get; private set; }

        /// <summary>TargetZOffset captured at tick 300 (Phase 4).</summary>
        public float Phase4TargetZOffset { get; private set; }

        // ── Phase latch flags ─────────────────────────────────────────────────

        private bool _phase1Passed;
        private bool _phase2Passed;
        private bool _phase3Passed;

        // ── Entity handle ─────────────────────────────────────────────────────

        private Entity _vehicle;

        // ── Terrain pipeline systems ──────────────────────────────────────────

        private TerrainQueryInitializationSystem? _initSystem;
        private TerrainQuerySubmitSystem?          _submitSystem;
        private TerrainQuerySolverSystem?          _solverSystem;
        private TerrainQueryResolutionSystem?      _resolutionSystem;
        private TransformSyncSystem?               _transformSync;

        // Held to dispose TerrainQueryBatchData NativeArrays on shutdown.
        private EntityRepository? _world;

        // ── IScenario ─────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string ScenarioName => ScenarioNames.TerrainClamping;

        /// <inheritdoc/>
        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            _world = world;

            // ── Component registration ─────────────────────────────────────────
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();
            world.RegisterComponent<GroundClampingConfig>();
            world.RegisterComponent<GroundClampingState>();
            world.RegisterComponent<NetworkTransform>();
            world.RegisterComponent<NetworkAuthority>();

            // ── Build terrain pipeline ─────────────────────────────────────────
            _initSystem       = new TerrainQueryInitializationSystem();
            _submitSystem     = new TerrainQuerySubmitSystem();
            _solverSystem     = new TerrainQuerySolverSystem(new MockTerrainProvider());
            _resolutionSystem = new TerrainQueryResolutionSystem();
            _transformSync    = new TransformSyncSystem(driveFromNetwork: true);

            // ── Entity spawning ────────────────────────────────────────────────
            _vehicle = SpawnVehicle(world);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Pipeline order per tick:
        /// <list type="number">
        ///   <item>Advance SimTransform.Position.X manually (bypass CarKinem).</item>
        ///   <item>Sync NetworkTransform.LastPosition = current position (prevents TransformSyncSystem from lerping toward origin).</item>
        ///   <item>Run terrain pipeline: Init → Submit → Solver → Resolution.</item>
        ///   <item>Flush ECB (applies GroundClampingState from Resolution).</item>
        ///   <item>Run TransformSyncSystem to lerp CurrentZOffset.</item>
        ///   <item>Flush ECB (applies smoothed CurrentZOffset).</item>
        ///   <item>Assert phase conditions.</item>
        /// </list>
        /// </remarks>
        public bool EvaluateTick(uint tick, EntityRepository world)
        {
            ISimulationView view = world;

            // ── 1. Advance vehicle position (bypass CarKinem) ─────────────────
            ref var tf = ref world.GetComponentRW<SimTransform>(_vehicle);
            tf.Position.X += PositionAdvanceM;

            // ── 2. Sync NetworkTransform so TransformSyncSystem lerps to current, not origin ──
            world.SetComponent(_vehicle, new NetworkTransform
            {
                LastPosition = tf.Position,
                LastRotation = tf.Rotation,
            });

            // ── 3. Run terrain pipeline ───────────────────────────────────────
            _initSystem!.Execute(view, FixedDt);       // Reset/create TerrainQueryBatchData
            _submitSystem!.Execute(view, FixedDt);     // Submit XY query for the vehicle
            _solverSystem!.Execute(view, FixedDt);     // Call MockTerrainProvider
            _resolutionSystem!.Execute(view, FixedDt); // Write GroundClampingState via ECB

            // ── 4. Flush ECB (applies TargetZOffset + LastValidIgAltitude) ────
            FlushEcb(world);

            // ── 5. TransformSyncSystem: lerp CurrentZOffset toward TargetZOffset ──
            _transformSync!.Execute(view, FixedDt);

            // ── 6. Flush ECB (applies CurrentZOffset) ─────────────────────────
            FlushEcb(world);

            // ── 6b. Break Z feedback: TransformSync writes SimTransform.Z from network Z +
            // CurrentZOffset, but TerrainQuerySubmit uses tf.Position.Z as ReferenceSimZ.
            // For this 2.5D DEM1 path we keep authoritative sim altitude at Z=0; offsets live
            // only in GroundClampingState (what the phase assertions inspect).
            ref var tfLevel = ref world.GetComponentRW<SimTransform>(_vehicle);
            tfLevel.Position.Z = 0f;
            world.SetComponent(_vehicle, new NetworkTransform
            {
                LastPosition = tfLevel.Position,
                LastRotation = tfLevel.Rotation,
            });

            // ── 7. Read final GroundClampingState for assertions ──────────────
            var state = world.GetComponent<GroundClampingState>(_vehicle);

            // ── Phase 1 (tick 10): flat zone — no clamping ────────────────────
            if (tick == 10 && !_phase1Passed)
            {
                Phase1CurrentZOffset = state.CurrentZOffset;

                if (state.CurrentZOffset >= 0.01f)
                    throw new ScenarioFailureException(1,
                        $"Phase 1 FAILED at tick {tick}: CurrentZOffset={state.CurrentZOffset:F4} expected < 0.01 (flat zone)");

                _phase1Passed = true;
            }

            // ── Phase 2 (tick 150): ramp zone — smoothing active ──────────────
            if (tick == 150 && !_phase2Passed)
            {
                Phase2TargetZOffset  = state.TargetZOffset;
                Phase2CurrentZOffset = state.CurrentZOffset;

                if (state.TargetZOffset <= 0.5f)
                    throw new ScenarioFailureException(2,
                        $"Phase 2 FAILED at tick {tick}: TargetZOffset={state.TargetZOffset:F4} expected > 0.5");

                if (state.CurrentZOffset >= state.TargetZOffset)
                    throw new ScenarioFailureException(2,
                        $"Phase 2 FAILED at tick {tick}: CurrentZOffset={state.CurrentZOffset:F4} >= TargetZOffset={state.TargetZOffset:F4} (smoothing should lag)");

                _phase2Passed = true;
            }

            // ── Phase 3 (tick 240): spike region — jump rejected ──────────────
            if (tick == 240 && !_phase3Passed)
            {
                Phase3LastValidIgAltitude = state.LastValidIgAltitude;

                if (state.LastValidIgAltitude >= 10f)
                    throw new ScenarioFailureException(3,
                        $"Phase 3 FAILED at tick {tick}: LastValidIgAltitude={state.LastValidIgAltitude:F4} expected < 10 (spike should have been rejected)");

                _phase3Passed = true;
            }

            // ── Phase 4 (tick 300): post-recovery — TargetZOffset ≈ 6.0 ─────
            if (tick == 300)
            {
                Phase4TargetZOffset = state.TargetZOffset;

                if (MathF.Abs(state.TargetZOffset - 6.0f) > SmoothedOffsetTolerance)
                    throw new ScenarioFailureException(4,
                        $"Phase 4 FAILED at tick {tick}: TargetZOffset={state.TargetZOffset:F4} expected 6.0 ±{SmoothedOffsetTolerance}");

                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }

        /// <inheritdoc/>
        public void OnShutdown()
        {
            if (_world == null) return;
            if (_world.HasSingleton<TerrainQueryBatchData>())
            {
                ref var b = ref _world.GetSingleton<TerrainQueryBatchData>();
                if (b.Requests.IsCreated) b.Requests.Dispose();
                if (b.Results.IsCreated)  b.Results.Dispose();
            }
        }

        // ── Entity factory ────────────────────────────────────────────────────

        private static Entity SpawnVehicle(EntityRepository world)
        {
            var e = world.CreateEntity();

            world.AddComponent(e, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });

            world.AddComponent(e, new SimVelocity
            {
                Linear = new Vector3(VehicleSpeedMs, 0f, 0f),
            });

            world.AddComponent(e, new GroundClampingConfig
            {
                Mode                 = EClampingMode.ForceOn,
                BaseRequiresClamping = 1,
            });

            world.AddComponent(e, new GroundClampingState
            {
                TargetZOffset       = 0f,
                CurrentZOffset      = 0f,
                LastValidIgAltitude = 0f, // First-frame bootstrap: accepts first hit unconditionally.
            });

            // Required by TransformSyncSystem for lerp-toward-network-position and Z-offset smoothing.
            world.AddComponent(e, new NetworkTransform
            {
                LastPosition = Vector3.Zero,
                LastRotation = Quaternion.Identity,
            });

            // LocalNodeId == PrimaryOwnerId → would be locally owned, but driveFromNetwork:true
            // forces SyncRemoteEntities for all entities regardless of ownership.
            world.AddComponent(e, new NetworkAuthority
            {
                LocalNodeId    = 0,
                PrimaryOwnerId = 0,
            });

            return e;
        }

        // ── Pipeline helper ───────────────────────────────────────────────────

        /// <summary>
        /// Plays back the per-thread command buffer into the world so ECB mutations
        /// (e.g. <see cref="GroundClampingState"/> writes from the terrain pipeline
        /// and <see cref="TransformSyncSystem"/>) are immediately visible.
        /// </summary>
        private static void FlushEcb(EntityRepository world)
        {
            var ecb = (EntityCommandBuffer)((ISimulationView)world).GetCommandBuffer();
            ecb.Playback(world);
        }
    }
}
