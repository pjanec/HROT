using System.Numerics;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Framework.Runner;
using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Components;
using ModuleHost.Core;

namespace Fdp.Examples.Common
{
    /// <summary>
    /// Wraps an <see cref="IScenario"/> as an <see cref="ISubsystem"/> so it plugs into
    /// <see cref="SubsystemOrchestrator"/>.
    ///
    /// <para>Lifecycle per tick:
    /// <list type="number">
    ///   <item>Advance <c>GlobalTime</c> via <see cref="SteppingTimeController"/> when deterministic.</item>
    ///   <item>Call <see cref="IScenario.EvaluateTick"/> so commands are injected into the event bus
    ///         before the kernel processes them this frame.</item>
    ///   <item>Call <see cref="ModuleHostKernel.Update"/>.</item>
    ///   <item>Check success / failure / timeout and invoke the exit callback.</item>
    /// </list>
    /// </para>
    ///
    /// <para>Exit codes: 0 = success, 1 = assertion failed, 2 = timed out.</para>
    /// </summary>
    public sealed class ScenarioSubsystem : ISubsystem, IMapCameraProvider
    {
        // ── Construction ──────────────────────────────────────────────────────

        private readonly IScenario _scenario;
        private readonly int _maxTicks;
        private readonly Action<int> _exitCallback;
        private readonly float _constructorDt;

        // ── Runtime state (set during Initialize) ─────────────────────────────

        private EntityRepository? _world;
        private ModuleHostKernel? _kernel;
        private SteppingTimeController? _timeController;
        private MapCanvas? _canvas;
        private SubsystemOrchestrator? _orchestrator;

        private bool _deterministic;
        private float _fixedDeltaSeconds;
        private uint _tick;

        // ── ISubsystem ────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string Name => $"ScenarioSubsystem[{_scenario.ScenarioName}]";

        /// <inheritdoc/>
        public Vector4 TitleBarColor => new Vector4(0.2f, 0.7f, 0.3f, 1.0f);

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates a ScenarioSubsystem.
        /// </summary>
        /// <param name="scenario">The scenario to execute.</param>
        /// <param name="maxTicks">Maximum number of ticks before a timeout exit(2).</param>
        /// <param name="exitCallback">
        /// Called with the exit code (0/1/2) instead of <see cref="Environment.Exit"/>.
        /// Pass <c>null</c> to use <see cref="Environment.Exit"/> (default for production).
        /// </param>
        /// <param name="fixedDeltaSeconds">
        /// Fixed simulation step in seconds. Used as fallback when
        /// <see cref="SubsystemConfig.FixedDeltaSeconds"/> is not yet available.
        /// Default = 1/60 s.
        /// </param>
        public ScenarioSubsystem(
            IScenario scenario,
            int maxTicks,
            Action<int>? exitCallback = null,
            float fixedDeltaSeconds = 1.0f / 60.0f)
        {
            _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            _maxTicks = maxTicks;
            _exitCallback = exitCallback ?? Environment.Exit;
            _constructorDt = fixedDeltaSeconds;
            _fixedDeltaSeconds = fixedDeltaSeconds;
        }

        /// <summary>
        /// Attaches the owning orchestrator so the subsystem can stop the run loop when the
        /// scenario exits. Must be called before <see cref="SubsystemOrchestrator.Run"/>.
        /// </summary>
        public void AttachOrchestrator(SubsystemOrchestrator orchestrator)
            => _orchestrator = orchestrator;

        // ── ISubsystem lifecycle ──────────────────────────────────────────────

        /// <inheritdoc/>
        public void Initialize(SubsystemConfig config)
        {
            _deterministic = config.Deterministic;
            _fixedDeltaSeconds = config.Deterministic ? config.FixedDeltaSeconds : _constructorDt;

            _world = new EntityRepository();
            var accumulator = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, accumulator);

            // Set time controller BEFORE Initialize (kernel requires it).
            var seedTime = new GlobalTime { TimeScale = 1.0f, DeltaTime = _fixedDeltaSeconds };
            _timeController = new SteppingTimeController(seedTime);
            _kernel.SetTimeController(_timeController);

            // Let the scenario register modules and spawn entities.
            _scenario.Configure(_world, _kernel);

            if (!config.Headless)
            {
                _canvas = new MapCanvas();
                _scenario.ConfigureVisuals(_canvas, _world);
            }

            _kernel.Initialize();

            FdpLog<ScenarioSubsystem>.Info("[{0}] === SCENARIO START tick=0", _scenario.ScenarioName);
        }

        /// <inheritdoc/>
        public void Update(float deltaTime)
        {
            _tick++;

            // Trace every tick so CI logs show execution progress.
            FdpLog<ScenarioSubsystem>.Trace("[{0}] tick={1}", _scenario.ScenarioName, _tick);

            // 1. Advance GlobalTime when running in deterministic mode.
            if (_deterministic)
            {
                var stepped = _timeController!.Step(_fixedDeltaSeconds);
                // Set singleton so EvaluateTick can read the updated DeltaTime this tick.
                _world!.SetSingletonUnmanaged(stepped);
            }

            // 2. EvaluateTick BEFORE kernel.Update() so injected events are processed this frame.
            bool success;
            try
            {
                success = _scenario.EvaluateTick(_tick, _world!);
            }
            catch (ScenarioFailureException ex)
            {
                FdpLog<ScenarioSubsystem>.Error(
                    "[{0}] [CI FAILURE] Phase {1} FAILED tick={2}: {3}",
                    _scenario.ScenarioName, ex.PhaseId, _tick, ex.Diagnostics);
                ExitWith(1);
                return;
            }

            // 3. Advance the kernel (processes injected events, runs all module systems).
            _kernel!.Update();

            // 4. Check completion after the kernel frame is done.
            if (success)
            {
                FdpLog<ScenarioSubsystem>.Info(
                    "[{0}] [CI SUCCESS] tick={1}", _scenario.ScenarioName, _tick);
                ExitWith(0);
                return;
            }

            if (_tick >= (uint)_maxTicks)
            {
                FdpLog<ScenarioSubsystem>.Error(
                    "[{0}] [CI TIMEOUT] after {1} ticks", _scenario.ScenarioName, _maxTicks);
                ExitWith(2);
            }
        }

        /// <inheritdoc/>
        public void DrawWorld() { }

        /// <inheritdoc/>
        public void DrawUI() { }

        /// <inheritdoc/>
        public void Shutdown()
        {
            _kernel?.Dispose();
            _world?.Dispose();
        }

        // ── IMapCameraProvider ────────────────────────────────────────────────

        /// <inheritdoc/>
        public MapCamera? GetMapCamera() => _canvas?.Camera;

        // ── Helpers ───────────────────────────────────────────────────────────

        private void ExitWith(int code)
        {
            // Stop the Run() loop first so the orchestrator can proceed to Shutdown().
            _orchestrator?.Stop();
            _exitCallback(code);
        }
    }
}
