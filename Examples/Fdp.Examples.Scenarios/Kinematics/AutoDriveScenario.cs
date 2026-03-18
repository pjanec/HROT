using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Road;
using CarKinem.Spatial;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Examples.Common;
using Fdp.Kernel;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Vis2D;
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;

namespace Fdp.Examples.Scenarios.Kinematics
{
    /// <summary>
    /// DEM1-D001 — AutoDrive: Ground kinematics + RVO collision avoidance.
    ///
    /// <para>Two vehicles (Alpha and Bravo) are spawned on a direct head-on collision course,
    /// pre-configured at cruise speed so the encounter geometry matches the phase timing table.
    /// The test verifies that the RVO solver deviates them laterally, they recover their
    /// original heading, and both arrive at their destinations within the tick budget.</para>
    ///
    /// <para>Phase table:</para>
    /// <list type="table">
    ///   <item><term>Phase 1 (tick 20)</term><description>Alpha speed &gt; 0, Y offset &lt; 0.5 m</description></item>
    ///   <item><term>Phase 2 (tick 70)</term><description>|Alpha.Y| &gt; 2.0 m — RVO lateral deviation</description></item>
    ///   <item><term>Phase 3 (tick 160)</term><description>|Alpha.Y| &lt; 2.0 m — recovery toward X-axis</description></item>
    ///   <item><term>Phase 4 (≤200)</term><description>Both vehicles: HasArrived == 1</description></item>
    /// </list>
    /// </summary>
    public sealed class AutoDriveScenario : IScenario
    {
        // ── Observable state for test assertions ──────────────────────────────

        /// <summary>Failure reason captured if a phase assertion fires (null if scenario succeeds).</summary>
        public string? FailureReason { get; private set; }

        /// <summary>Alpha's speed (m/s) captured at tick 20.</summary>
        public float AlphaSpeedAtTick20 { get; private set; }

        /// <summary>Alpha's Y position (m) captured at tick 70.</summary>
        public float AlphaYAtTick70 { get; private set; }

        /// <summary>Alpha's Y position (m) captured at tick 160 (recovery check).</summary>
        public float AlphaYAtTick160 { get; private set; }

        /// <summary>Whether both vehicles have arrived at their destinations by tick 200.</summary>
        public bool BothArrivedByTick200 { get; private set; }

        // ── Phase latches (fail-fast guards) ─────────────────────────────────

        private bool _phase1Checked;
        private bool _phase2Checked;
        private bool _phase3Checked;

        // ── Entity handles ────────────────────────────────────────────────────

        private Entity _alpha;
        private Entity _bravo;

        // ── Scenario destinations ─────────────────────────────────────────────

        private static readonly Vector2 AlphaStart = new(-15f, 0f);
        private static readonly Vector2 AlphaDest  = new( 15f, 0f);
        private static readonly Vector2 BravoStart = new( 15f, 0f);
        private static readonly Vector2 BravoDest  = new(-15f, 0f);
        private const float DriveSpeed     = 15f;   // m/s — avoidance zone entry at ~tick 40 (10m threshold)
        private const float ArrivalRadius  = 2.0f;

        // ── IScenario ─────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string ScenarioName => "autodrive";

        /// <inheritdoc/>
        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // ── Component registration ────────────────────────────────────────
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<VehicleState>();
            world.RegisterComponent<VehicleParams>();
            world.RegisterComponent<NavState>();
            world.RegisterComponent<SimVelocity>();
            world.RegisterComponent<SpatialGridData>();
            world.RegisterComponent<PhysicsCollider>(); // required by SpatialHashSystem filter (BATCH-05 Task 2)

            // ── Systems ───────────────────────────────────────────────────────
            var spatialHash = new SpatialHashSystem();
            var kinematics  = new CarKinematicsSystem(new RoadNetworkBlob(), new TrajectoryPoolManager())
            {
                ForceSerial = true   // deterministic: no parallel partitioning in CI
            };

            spatialHash.Create(world);
            kinematics.Create(world);

            kernel.RegisterModule(new DirectSystemsModule("AutoDriveModule", spatialHash, kinematics));

            // ── Entity spawning ───────────────────────────────────────────────
            // NavState is set directly at spawn so vehicles start at cruise speed from tick 1.
            _alpha = SpawnVehicle(world, AlphaStart, AlphaDest, yawRadians: 0f);
            _bravo = SpawnVehicle(world, BravoStart, BravoDest, yawRadians: MathF.PI);
        }

        /// <inheritdoc/>
        public bool EvaluateTick(uint tick, EntityRepository world)
        {
            // ── Diagnostic snapshots ──────────────────────────────────────────
            // ── Phase 1 (tick 20): vehicles accelerating, still on X-axis ─────
            if (tick == 20 && !_phase1Checked)
            {
                _phase1Checked = true;
                var vel  = world.GetComponent<SimVelocity>(_alpha);
                var tf   = world.GetComponent<SimTransform>(_alpha);
                float speed = vel.Linear.Length();
                AlphaSpeedAtTick20 = speed;

                if (speed <= 0f)
                {
                    FailureReason = $"Phase 1 FAILED: Alpha speed={speed:F3} expected >0 at tick 20";
                    throw new ScenarioFailureException(1, FailureReason);
                }
                if (MathF.Abs(tf.Position.Y) >= 0.5f)
                {
                    FailureReason = $"Phase 1 FAILED: Alpha Y={tf.Position.Y:F3} expected <0.5 m at tick 20";
                    throw new ScenarioFailureException(1, FailureReason);
                }
            }

            // ── Phase 2 (tick 70): RVO lateral deviation ──────────────────────
            if (tick == 70 && !_phase2Checked)
            {
                _phase2Checked = true;
                var tf = world.GetComponent<SimTransform>(_alpha);
                AlphaYAtTick70 = tf.Position.Y;

                if (MathF.Abs(AlphaYAtTick70) <= 2.0f)
                {
                    FailureReason = $"Phase 2 FAILED: Alpha |Y|={MathF.Abs(AlphaYAtTick70):F3} expected >2.0 m at tick 70";
                    throw new ScenarioFailureException(2, FailureReason);
                }
            }

            // ── Phase 3 (tick 160): recovering toward axis ────────────────────
            if (tick == 160 && !_phase3Checked)
            {
                _phase3Checked = true;
                var tf = world.GetComponent<SimTransform>(_alpha);
                AlphaYAtTick160 = tf.Position.Y;

                if (MathF.Abs(AlphaYAtTick160) >= 2.0f)
                {
                    FailureReason = $"Phase 3 FAILED: Alpha |Y|={MathF.Abs(AlphaYAtTick160):F3} expected <2.0 m at tick 160";
                    throw new ScenarioFailureException(3, FailureReason);
                }
            }

            // ── Phase 4 (≤200): both vehicles arrived ──────────────────────────
            if (tick <= 200)
            {
                var navAlpha = world.GetComponent<NavState>(_alpha);
                var navBravo = world.GetComponent<NavState>(_bravo);

                if (navAlpha.HasArrived == 1 && navBravo.HasArrived == 1)
                {
                    BothArrivedByTick200 = true;
                    return true;  // CI SUCCESS
                }
            }
            else if (tick > 200)
            {
                FailureReason = $"Phase 4 FAILED: Both vehicles did not arrive within 200 ticks. " +
                    $"Alpha.HasArrived={world.GetComponent<NavState>(_alpha).HasArrived} " +
                    $"Bravo.HasArrived={world.GetComponent<NavState>(_bravo).HasArrived}";
                throw new ScenarioFailureException(4, FailureReason);
            }

            return false;
        }

        /// <inheritdoc/>
        public void ConfigureVisuals(FDP.Toolkit.Vis2D.MapCanvas? canvas, EntityRepository world) { }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Entity SpawnVehicle(EntityRepository world, Vector2 position, Vector2 destination, float yawRadians)
        {
            var e = world.CreateEntity();

            // Forward direction from yaw (yaw=0 → +X, yaw=π → -X)
            var fwd = new Vector2(MathF.Cos(yawRadians), MathF.Sin(yawRadians));

            world.AddComponent(e, new SimTransform
            {
                Position = new Vector3(position.X, position.Y, 0f),
                Rotation = SimMath.FromYaw(yawRadians)
            });
            // Mark as locally-owned so CarKinematicsSystem's WithOwned<SimTransform> filter passes.
            world.SetAuthority<SimTransform>(e, true);

            // Pre-set velocity to cruise speed so Phase 1 check (tick 20) sees speed > 0 immediately.
            world.AddComponent(e, new SimVelocity
            {
                Linear  = new Vector3(fwd.X * DriveSpeed, fwd.Y * DriveSpeed, 0f),
                Angular = Vector3.Zero
            });

            // PersonalCar preset with boosted avoidance radius so RVO detects the head-on
            // approach well before vehicles overlap.
            var prms = VehiclePresets.GetPreset(VehicleClass.PersonalCar);
            prms.AvoidanceRadius = 4.0f;   // query radius = 4.0 × 2.5 = 10 m
            world.AddComponent(e, prms);

            // Pre-set speed in VehicleState to match initial SimVelocity.
            world.AddComponent(e, new VehicleState { Speed = DriveSpeed });

            // PhysicsCollider is required so SpatialHashSystem (BATCH-05 Task 2) indexes
            // this entity for broadphase neighbor queries used by RVO avoidance.
            world.AddComponent(e, new PhysicsCollider { Radius = 2.0f, CollisionLayer = 1 });

            // Set NavState directly — no CmdNavigateToPoint bus event required.
            world.AddComponent(e, new NavState
            {
                Mode             = KinematicsMode.None,
                FinalDestination = destination,
                ArrivalRadius    = ArrivalRadius,
                TargetSpeed      = DriveSpeed,
                HasArrived       = 0,
                TrajectoryId     = -1,
                CurrentSegmentId = -1,
            });

            return e;
        }

        // ── Inner module helper ───────────────────────────────────────────────

        /// <summary>
        /// Minimal <see cref="IModule"/> adapter that calls a list of
        /// <see cref="ComponentSystem"/> instances on the main thread each tick.
        /// Uses <c>ExecutionPolicy.Synchronous()</c> (DataStrategy.Direct) so the kernel
        /// passes the live <see cref="EntityRepository"/> directly to the module.
        /// </summary>
        private sealed class DirectSystemsModule : IModule
        {
            private readonly ComponentSystem[] _systems;

            public string Name { get; }
            public ExecutionPolicy Policy     => ExecutionPolicy.Synchronous();
            public IReadOnlyList<Type>? WatchComponents => null;
            public IReadOnlyList<Type>? WatchEvents     => null;

            public DirectSystemsModule(string name, params ComponentSystem[] systems)
            {
                Name     = name;
                _systems = systems;
            }

            public void RegisterSystems(ISystemRegistry registry) { }

            public void Tick(ISimulationView view, float deltaTime)
            {
                // Systems were Create()'d with the live EntityRepository; Run() uses
                // the same stored World reference — view is the same object (Direct strategy).
                foreach (var sys in _systems)
                    sys.Run();
            }

            public IReadOnlyList<Type>? GetRequiredComponents() => null;
        }
    }
}
