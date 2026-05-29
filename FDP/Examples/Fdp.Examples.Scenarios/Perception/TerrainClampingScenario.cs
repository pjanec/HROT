using System;
using System.Numerics;
using Fdp.Examples.Common;
using Fdp.Examples.Common.Constants;
using Fdp.Examples.Common.Helpers;
using Fdp.Examples.Common.Systems;
using Fdp.Core;
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
    /// DEM1-D007 — TerrainClamping: prove async terrain batching, authoritative altitude, and
    /// jump-rejection via the <c>FDP.Toolkit.Geographic</c> pipeline systems.
    ///
    /// <para>Since the 3D Cognitive Spatial Awareness promotion (P3D-102) the terrain hit is
    /// written straight into the authoritative <c>SimTransform.Position.Z</c> — there is no visual
    /// offset. This scenario therefore asserts the authoritative Z directly.</para>
    ///
    /// <para><b>Topology (pipeline driven manually in EvaluateTick):</b></para>
    /// <list type="number">
    ///   <item><see cref="TerrainQueryInitializationSystem"/> — reset batch count.</item>
    ///   <item><see cref="TerrainQuerySubmitSystem"/> — submit queries for clamped entities.</item>
    ///   <item><see cref="TerrainQuerySolverSystem"/> — invoke <see cref="MockTerrainProvider"/>.</item>
    ///   <item><see cref="TerrainQueryResolutionSystem"/> — write HitZ → Position.Z; jump-rejection.</item>
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
    ///   <item><term>Phase 1 (tick 10)</term><description>X ≈ 1.7 m — flat zone; Position.Z &lt; 0.01.</description></item>
    ///   <item><term>Phase 2 (tick 150)</term><description>X ≈ 25 m — ramp; Position.Z ≈ 1.0 (&gt; 0.5).</description></item>
    ///   <item><term>Phase 3 (tick 240)</term><description>X ≈ 40 m — spike rejected; LastValidIgAltitude &lt; 10 AND Position.Z &lt; 10.</description></item>
    ///   <item><term>Phase 4 (tick 300)</term><description>X ≈ 50 m — recovery; Position.Z ≈ 6.0 (±1.0) → success.</description></item>
    /// </list>
    /// </summary>
    public sealed class TerrainClampingScenario : IScenario
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const float VehicleSpeedMs    = 10f;         // m/s along X axis
        private const float FixedDt           = 1f / 60f;    // 60 Hz deterministic step
        private const float PositionAdvanceM  = VehicleSpeedMs * FixedDt; // ≈ 0.167 m/tick
        private const float AltitudeTolerance = 1.0f;        // ±1.0 m for Phase 4 assertion

        // ── Observable state for test assertions ──────────────────────────────

        /// <summary>Authoritative Position.Z captured at tick 10 (Phase 1, flat).</summary>
        public float Phase1Z { get; private set; }

        /// <summary>Authoritative Position.Z captured at tick 150 (Phase 2, ramp).</summary>
        public float Phase2Z { get; private set; }

        /// <summary>LastValidIgAltitude captured at tick 240 (Phase 3, spike rejected).</summary>
        public float Phase3LastValidIgAltitude { get; private set; }

        /// <summary>Authoritative Position.Z captured at tick 240 (Phase 3, spike rejected).</summary>
        public float Phase3Z { get; private set; }

        /// <summary>Authoritative Position.Z captured at tick 300 (Phase 4, recovery).</summary>
        public float Phase4Z { get; private set; }

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
            world.RegisterComponent<TerrainClampBaseline>();

            // ── Build terrain pipeline ─────────────────────────────────────────
            _initSystem       = new TerrainQueryInitializationSystem();
            _submitSystem     = new TerrainQuerySubmitSystem();
            _solverSystem     = new TerrainQuerySolverSystem(new MockTerrainProvider());
            _resolutionSystem = new TerrainQueryResolutionSystem();

            // ── Entity spawning ────────────────────────────────────────────────
            _vehicle = SpawnVehicle(world);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Pipeline order per tick:
        /// <list type="number">
        ///   <item>Advance SimTransform.Position.X manually (bypass CarKinem).</item>
        ///   <item>Run terrain pipeline: Init → Submit → Solver → Resolution.</item>
        ///   <item>Flush ECB (applies authoritative Position.Z + TerrainClampBaseline).</item>
        ///   <item>Assert phase conditions on the authoritative Z.</item>
        /// </list>
        /// </remarks>
        public bool EvaluateTick(uint tick, EntityRepository world)
        {
            ISimulationView view = world;

            // ── 1. Advance vehicle position (bypass CarKinem) ─────────────────
            ref var tf = ref world.GetComponentRW<SimTransform>(_vehicle);
            tf.Position.X += PositionAdvanceM;

            // ── 2. Run terrain pipeline ───────────────────────────────────────
            _initSystem!.Execute(view, FixedDt);       // Reset/create TerrainQueryBatchData
            _submitSystem!.Execute(view, FixedDt);     // Submit XY query for the vehicle
            _solverSystem!.Execute(view, FixedDt);     // Call MockTerrainProvider
            _resolutionSystem!.Execute(view, FixedDt); // Write HitZ → SimTransform.Position.Z via ECB

            // ── 3. Flush ECB (applies authoritative Z + baseline) ─────────────
            FlushEcb(world);

            // ── 4. Read final authoritative state for assertions ──────────────
            var state    = world.GetComponent<TerrainClampBaseline>(_vehicle);
            float posZ   = world.GetComponent<SimTransform>(_vehicle).Position.Z;

            // ── Phase 1 (tick 10): flat zone — authoritative Z ≈ 0 ────────────
            if (tick == 10 && !_phase1Passed)
            {
                Phase1Z = posZ;
                if (posZ >= 0.01f)
                    throw new ScenarioFailureException(1,
                        $"Phase 1 FAILED at tick {tick}: Position.Z={posZ:F4} expected < 0.01 (flat zone)");
                _phase1Passed = true;
            }

            // ── Phase 2 (tick 150): ramp zone — authoritative Z rising ────────
            if (tick == 150 && !_phase2Passed)
            {
                Phase2Z = posZ;
                if (posZ <= 0.5f)
                    throw new ScenarioFailureException(2,
                        $"Phase 2 FAILED at tick {tick}: Position.Z={posZ:F4} expected > 0.5 (ramp)");
                _phase2Passed = true;
            }

            // ── Phase 3 (tick 240): spike region — jump rejected ──────────────
            if (tick == 240 && !_phase3Passed)
            {
                Phase3LastValidIgAltitude = state.LastValidIgAltitude;
                Phase3Z = posZ;

                if (state.LastValidIgAltitude >= 10f)
                    throw new ScenarioFailureException(3,
                        $"Phase 3 FAILED at tick {tick}: LastValidIgAltitude={state.LastValidIgAltitude:F4} expected < 10 (spike should have been rejected)");
                if (posZ >= 10f)
                    throw new ScenarioFailureException(3,
                        $"Phase 3 FAILED at tick {tick}: Position.Z={posZ:F4} expected < 10 (spike should not have been applied)");
                _phase3Passed = true;
            }

            // ── Phase 4 (tick 300): post-recovery — authoritative Z ≈ 6.0 ─────
            if (tick == 300)
            {
                Phase4Z = posZ;
                if (MathF.Abs(posZ - 6.0f) > AltitudeTolerance)
                    throw new ScenarioFailureException(4,
                        $"Phase 4 FAILED at tick {tick}: Position.Z={posZ:F4} expected 6.0 ±{AltitudeTolerance}");
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

            world.AddComponent(e, new TerrainClampBaseline
            {
                LastValidIgAltitude = 0f, // First-frame bootstrap: accepts first hit unconditionally.
                IgAltitudeBaselineEstablished = 0,
            });

            return e;
        }

        // ── Pipeline helper ───────────────────────────────────────────────────

        /// <summary>
        /// Plays back the per-thread command buffer into the world so ECB mutations
        /// (authoritative <c>SimTransform.Position.Z</c> and <see cref="TerrainClampBaseline"/>
        /// writes from the terrain pipeline) are immediately visible.
        /// </summary>
        private static void FlushEcb(EntityRepository world)
        {
            var ecb = (EntityCommandBuffer)((ISimulationView)world).GetCommandBuffer();
            ecb.Playback(world);
        }
    }
}
