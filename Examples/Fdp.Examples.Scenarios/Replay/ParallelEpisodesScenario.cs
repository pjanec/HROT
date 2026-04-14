using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Road;
using CarKinem.Spatial;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Examples.Common;
using Fdp.Examples.Common.Constants;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.CarKinem.Systems;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Replay;
using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Vis2D;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Examples.Scenarios.Replay
{
    /// <summary>
    /// DEM1-D008 — ParallelEpisodes: prove <c>Fdp.Kernel.FlightRecorder</c> LZ4 recording and
    /// naked-node replay produce bit-identical positions.
    ///
    /// <para><b>Phase A (Configure, synchronous):</b> A separate live world + kernel drives a
    /// vehicle with <c>LiveKinematicsModule</c> for <see cref="LiveRunTicks"/> ticks using a
    /// <c>SteppingTimeController</c>.  Recording is handled by a
    /// <c>RecordingModule</c> configured with <c>Blocking = true</c> — this routes each frame
    /// through <c>RecorderTickSystem</c> in <c>PostSimulation</c> and blocks until the
    /// front-buffer swap completes, preventing delta-frame drops in the CPU-bound loop.
    /// The trajectory is captured into <see cref="_livePositions"/> and the module is disposed
    /// after the loop (flushing the LZ4 buffer and writing the <c>.fdprec</c> manifest).</para>
    ///
    /// <para><b>Phase B (main loop):</b> The main scenario kernel registers only a
    /// <c>ReplayModule</c> — no kinematics.  <see cref="EvaluateTick"/> compares the
    /// replayed <see cref="SimTransform"/> against the stored live trajectory at logical
    /// frames 25 and 50 to within <see cref="PositionTolerance"/> metres.</para>
    ///
    /// <para><b>Phase table:</b></para>
    /// <list type="table">
    ///   <item><term>Phase 1 (tick 26)</term><description>|livePos[25] − replayPos| &lt; 0.001 m</description></item>
    ///   <item><term>Phase 2 (tick 51)</term><description>|livePos[50] − replayPos| &lt; 0.001 m → SUCCESS</description></item>
    /// </list>
    /// </summary>
    public sealed class ParallelEpisodesScenario : IScenario
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Number of ticks the live kernel runs before replay begins.</summary>
        public const int LiveRunTicks = 50;

        /// <summary>Fixed simulation delta used for both live and replay phases.</summary>
        public const float FixedDelta = 1.0f / 60.0f;

        /// <summary>Tolerance in metres for the live-vs-replay position comparison.</summary>
        public const float PositionTolerance = 0.001f;

        private const float DriveSpeed   = 15.0f;   // m/s
        private const float ArrivalRadius = 5.0f;

        // ── Observable state for test assertions ──────────────────────────────

        /// <summary>
        /// True after the frame-25 comparison passes the <see cref="PositionTolerance"/> threshold.
        /// </summary>
        public bool ReplayMatchedLiveAtTick25 { get; private set; }

        /// <summary>
        /// Snapshot of module type names registered in the replay (main) kernel after
        /// <see cref="Configure"/> completes.  Tests use this to verify that no kinematics
        /// module was registered — proving positions come purely from <c>ReplayModule</c>.
        /// </summary>
        public IReadOnlyList<string> ReplayKernelModuleTypeNames { get; private set; } =
            Array.Empty<string>();

        // ── Private state ─────────────────────────────────────────────────────

        /// <summary>Live trajectory positions, keyed by 1-based tick index.</summary>
        private readonly Dictionary<uint, Vector3> _livePositions = new();

        /// <summary>Path of the temporary <c>.fdprec</c> recording file.</summary>
        private string _recFilePath = string.Empty;

        /// <summary>The entity handle of the replayed vehicle in the main world.</summary>
        private Entity _replayVehicle;

        // ── IScenario ─────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string ScenarioName => ScenarioNames.ParallelEpisodes;

        /// <inheritdoc/>
        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            _recFilePath = Path.Combine(
                Path.GetTempPath(),
                $"parallelepisodes_{Guid.NewGuid():N}.fdprec");

            // ── Phase A: live recording ───────────────────────────────────────
            RunLivePhase(_recFilePath, _livePositions, out _replayVehicle);

            // ── Phase B: register replay on the main kernel ───────────────────
            RegisterComponents(world);
            kernel.RegisterModule(new ReplayModule(_recFilePath, world));

            // Capture the kernel's module topology for test introspection.
            // ScenarioSubsystem calls Initialize() after Configure() returns, so we must
            // snapshot the registered type names here before Initialize() is called.
            ReplayKernelModuleTypeNames = kernel.GetRegisteredModuleTypeNames();
        }

        /// <inheritdoc/>
        public bool EvaluateTick(uint tick, EntityRepository world)
        {
            // After EvaluateTick(N) kernel.Update() applies replay frame N.
            // At EvaluateTick(N+1) the world contains the result of replay frame N.
            // → frame 25 data is visible at tick=26; frame 50 data at tick=51.

            // Phase 1: verify replay matches live at frame 25
            if (tick == 26 && !ReplayMatchedLiveAtTick25)
            {
                if (!world.IsAlive(_replayVehicle))
                    throw new ScenarioFailureException(1,
                        $"[Phase 1 FAILED] Replay vehicle entity not alive at tick {tick}.");

                var replayTf = world.GetComponent<SimTransform>(_replayVehicle);
                var liveTf   = _livePositions[25];
                float dist   = Vector3.Distance(replayTf.Position, liveTf);

                if (dist >= PositionTolerance)
                    throw new ScenarioFailureException(1,
                        $"[Phase 1 FAILED] tick={tick} |live[25]-replay|={dist:F6} m (>= {PositionTolerance})  " +
                        $"live={liveTf} replay={replayTf.Position}");

                ReplayMatchedLiveAtTick25 = true;
                FdpLog<ParallelEpisodesScenario>.Info(
                    "[{0}] Phase 1 PASSED tick={1} |live[25]-replay|={2:F6} m",
                    ScenarioName, tick, dist);
            }

            // Phase 2: verify replay matches live at frame 50, then succeed
            if (tick == 51)
            {
                if (!world.IsAlive(_replayVehicle))
                    throw new ScenarioFailureException(2,
                        $"[Phase 2 FAILED] Replay vehicle entity not alive at tick {tick}.");

                var replayTf = world.GetComponent<SimTransform>(_replayVehicle);
                var liveTf   = _livePositions[50];
                float dist   = Vector3.Distance(replayTf.Position, liveTf);

                if (dist >= PositionTolerance)
                    throw new ScenarioFailureException(2,
                        $"[Phase 2 FAILED] tick={tick} |live[50]-replay|={dist:F6} m (>= {PositionTolerance})  " +
                        $"live={liveTf} replay={replayTf.Position}");

                FdpLog<ParallelEpisodesScenario>.Info(
                    "[{0}] Phase 2 PASSED tick={1} |live[50]-replay|={2:F6} m",
                    ScenarioName, tick, dist);

                return true;  // CI SUCCESS
            }

            return false;
        }

        /// <inheritdoc/>
        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }

        /// <inheritdoc/>
        public void OnShutdown()
        {
            // Best-effort cleanup of the temporary recording file.
            if (!string.IsNullOrEmpty(_recFilePath))
            {
                try { File.Delete(_recFilePath); } catch { /* best-effort */ }
            }
        }

        // ── Live phase helper ─────────────────────────────────────────────────

        /// <summary>
        /// Creates a self-contained live world + kernel, drives the vehicle for
        /// <see cref="LiveRunTicks"/> deterministic ticks, records the trajectory into
        /// <paramref name="positions"/>, flushes the LZ4 buffer, and sets
        /// <paramref name="vehicleEntityId"/> to the entity handle that was recorded so
        /// the replay world can use the same ID.
        /// </summary>
        private static void RunLivePhase(
            string recFilePath,
            Dictionary<uint, Vector3> positions,
            out Entity vehicleEntityId)
        {
            using var liveWorld = new EntityRepository();
            RegisterComponents(liveWorld);

            var liveAccumulator = new EventAccumulator();

            // RecordingModule is declared before liveKernel so LIFO using-disposal gives
            // the order: liveKernel (stops ticks) → recordingModule (flush) → liveWorld.
            using var recordingModule = new RecordingModule(new RecordingConfiguration
            {
                FilePath = recFilePath,
                ExerciseId  = Guid.NewGuid(),
                Blocking = true    // prevents delta-frame drops in CPU-bound tight loop
            });
            using var liveKernel = new ModuleHostKernel(liveWorld, liveAccumulator);

            var seedTime      = new GlobalTime { TimeScale = 1.0f, DeltaTime = FixedDelta };
            var liveTimeCtrl  = new SteppingTimeController(seedTime);
            liveKernel.SetTimeController(liveTimeCtrl);

            // ── Kinematics module (DirectSystems pattern matching AutoDriveScenario) ─
            var spatialHash = new SpatialHashSystem();
            var kinematics  = new CarKinematicsSystem(new TrajectoryPoolManager())
            {
                ForceSerial = true   // deterministic: single-threaded partition-free
            };
            spatialHash.Create(liveWorld);
            kinematics.Create(liveWorld);
            liveKernel.RegisterModule(new LiveKinematicsModule(spatialHash, kinematics));

            // RecorderTickSystem (PostSimulation) captures each frame automatically.
            liveKernel.RegisterModule(recordingModule);

            // ── Spawn vehicle ────────────────────────────────────────────────
            vehicleEntityId = SpawnVehicle(liveWorld);
            var capturedId  = vehicleEntityId;  // capture for loop closure

            liveKernel.Initialize();

            // ── Tick loop — RecordingModule captures each frame via PostSimulation ──
            for (int t = 1; t <= LiveRunTicks; t++)
            {
                liveTimeCtrl.Step(FixedDelta);
                liveKernel.Update();

                // Capture position AFTER kinematics updated it.
                var tf = liveWorld.GetComponent<SimTransform>(capturedId);
                positions[(uint)t] = tf.Position;
            }

            // LIFO disposal: liveKernel → recordingModule.Dispose() flushes to disk → liveWorld.
        }

        // ── Entity / component helpers ────────────────────────────────────────

        private static void RegisterComponents(EntityRepository world)
        {
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();
            world.RegisterComponent<VehicleState>();
            world.RegisterComponent<VehicleParams>();
            world.RegisterComponent<NavState>();
            world.RegisterComponent<SpatialGridData>();
            world.RegisterComponent<PhysicsCollider>();
        }

        private static Entity SpawnVehicle(EntityRepository world)
        {
            var e   = world.CreateEntity();

            world.AddComponent(e, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = SimMath.FacingEast   // yaw=0 → +X
            });
            // Mark as locally-owned so CarKinematicsSystem's WithOwned<SimTransform> filter passes.
            world.SetAuthority<SimTransform>(e, true);

            world.AddComponent(e, new SimVelocity
            {
                Linear  = new Vector3(DriveSpeed, 0f, 0f),
                Angular = Vector3.Zero
            });

            var prms = VehiclePresets.GetPreset(VehicleClass.PersonalCar);
            world.AddComponent(e, prms);

            world.AddComponent(e, new VehicleState { Speed = DriveSpeed });

            world.AddComponent(e, new PhysicsCollider { Radius = 2.0f, CollisionLayer = 1 });

            world.AddComponent(e, new NavState
            {
                Mode             = KinematicsMode.None,
                FinalDestination = new Vector2(500f, 0f),   // far enough: never arrives in 50 ticks
                ArrivalRadius    = ArrivalRadius,
                TargetSpeed      = DriveSpeed,
                HasArrived       = 0,
                TrajectoryId     = -1,
                CurrentSegmentId = -1,
            });

            world.AddComponent(e, new SpatialGridData());

            return e;
        }

        // ── Inner module: wraps ground kinematics systems ─────────────────────

        /// <summary>
        /// Minimal <see cref="IEcsModule"/> adapter that runs <see cref="SpatialHashSystem"/>
        /// and <see cref="CarKinematicsSystem"/> directly on the live world.
        /// Uses <c>ExecutionPolicy.Synchronous()</c> (DataStrategy.Direct) so the kernel
        /// passes the live <see cref="EntityRepository"/> without snapshot overhead.
        /// </summary>
        private sealed class LiveKinematicsModule : IEcsModule
        {
            private readonly SpatialHashSystem    _spatial;
            private readonly CarKinematicsSystem  _kinematics;

            public string Name                      => "LiveKinematics_ParallelEpisodes";
            public ExecutionPolicy Policy           => ExecutionPolicy.Synchronous();
            public IReadOnlyList<Type>? WatchComponents => null;
            public IReadOnlyList<Type>? WatchEvents     => null;

            public LiveKinematicsModule(SpatialHashSystem spatial, CarKinematicsSystem kinematics)
            {
                _spatial    = spatial;
                _kinematics = kinematics;
            }

            public void RegisterSystems(ISystemRegistry registry) { }

            public void Tick(ISimulationView view, float deltaTime)
            {
                _spatial.Run();
                _kinematics.Run();
            }

            public IReadOnlyList<Type>? GetRequiredComponents() => null;
        }
    }
}
