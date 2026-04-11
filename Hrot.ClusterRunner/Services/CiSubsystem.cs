using Hrot.ClusterRunner.Scenarios;
using Fdp.Examples.Common;
using Fdp.Engine.Runner;

namespace Hrot.ClusterRunner.Services;

/// <summary>
/// Hosts a <see cref="ScenarioSubsystem"/> for the headless <c>--mode ci</c> path.
///
/// <para>The outer <see cref="SubsystemOrchestrator"/> drives this subsystem via the
/// standard <see cref="ISubsystem"/> lifecycle; this class creates and delegates every
/// call to an inner <see cref="ScenarioSubsystem"/> so the full deterministic CI harness
/// runs inside the Runner's normal orchestration loop.</para>
///
/// <para>When the scenario completes, <see cref="ScenarioSubsystem"/> calls
/// <c>orchestrator.Stop()</c> (injected via <see cref="AttachOrchestrator"/>), which
/// unwinds the run loop and ultimately returns control to <see cref="Program"/>'s
/// <c>Environment.Exit(code)</c>.</para>
/// </summary>
internal sealed class CiSubsystem : ISubsystem
{
    private readonly string _scenarioName;
    private ScenarioSubsystem? _inner;
    private SubsystemOrchestrator? _orchestrator;

    private const float FixedDeltaSeconds = 1.0f / 60.0f;
    /// <summary>
    /// Tick budget: 600 scenario ticks + generous headroom.
    /// At 60 Hz deterministic, 2400 ticks = 40 s — inside the 30 s wall-clock limit.
    /// </summary>
    private const int MaxTicks = 2400;

    public string Name => "CI";
    public System.Numerics.Vector4 TitleBarColor => new(0.3f, 0.6f, 0.2f, 1f);

    /// <param name="scenarioName">
    /// Case-insensitive scenario name supplied via <c>--scenario</c>.
    /// Must resolve to a registered CI scenario.
    /// </param>
    public CiSubsystem(string scenarioName)
    {
        _scenarioName = scenarioName ?? throw new ArgumentNullException(nameof(scenarioName));
    }

    /// <summary>
    /// Stores the owning orchestrator so it can be forwarded to the inner
    /// <see cref="ScenarioSubsystem"/> after the subsystem is created during
    /// <see cref="Initialize"/>. Must be called before <see cref="Initialize"/>.
    /// </summary>
    public void AttachOrchestrator(SubsystemOrchestrator orchestrator)
        => _orchestrator = orchestrator;

    /// <inheritdoc/>
    public void Initialize(SubsystemConfig config)
    {
        var scenario = CreateScenario(_scenarioName);
        _inner = new ScenarioSubsystem(
            scenario,
            maxTicks: MaxTicks,
            exitCallback: null,       // use Environment.Exit — terminates the CI process with the scenario result code
            fixedDeltaSeconds: FixedDeltaSeconds);

        if (_orchestrator is not null)
            _inner.AttachOrchestrator(_orchestrator);

        _inner.Initialize(config);
    }

    /// <inheritdoc/>
    public void Update(float deltaTime) => _inner?.Update(deltaTime);

    /// <inheritdoc/>
    public void DrawWorld() { }

    /// <inheritdoc/>
    public void DrawUI() { }

    /// <inheritdoc/>
    public void Shutdown() { }

    // ── Scenario registry ─────────────────────────────────────────────────

    private static IScenario CreateScenario(string name) =>
        name.ToLowerInvariant() switch
        {
            MinimalCIScenario.Key => new MinimalCIScenario(),
            _ => throw new ArgumentException(
                $"Unknown CI scenario '{name}'. Registered keys: {MinimalCIScenario.Key}",
                nameof(name))
        };
}
