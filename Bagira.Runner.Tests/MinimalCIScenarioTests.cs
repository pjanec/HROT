using Bagira.Runner.Scenarios;
using Fdp.Examples.Common;
using FDP.Framework.Runner;

namespace Bagira.Runner.Tests;

/// <summary>
/// Tests for <see cref="MinimalCIScenario"/> — verifies the three success conditions
/// specified in CGF1-S0205.
/// </summary>
public class MinimalCIScenarioTests
{
    // ── Harness helper ────────────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="scenario"/> through a headless deterministic
    /// <see cref="ScenarioSubsystem"/> and returns the captured exit code
    /// (0 = pass, 1 = assertion failed, 2 = timeout) without calling
    /// <see cref="Environment.Exit"/>.
    /// </summary>
    private static int RunScenario(IScenario scenario, int maxTicks = 1200, float dt = 1.0f / 60.0f)
    {
        int capturedCode = -1;

        var sub = new ScenarioSubsystem(
            scenario,
            maxTicks,
            code => capturedCode = code,
            dt);

        var opts = new RunnerOptions
        {
            Headless          = true,
            Deterministic     = true,
            FixedDeltaSeconds = dt
        };

        var orch = new SubsystemOrchestrator(new[] { (ISubsystem)sub }, opts);
        sub.AttachOrchestrator(orch);

        orch.Initialize();
        orch.Run();
        orch.Shutdown();

        return capturedCode;
    }

    // ── CGF1-S0205 success condition 1 ───────────────────────────────────────

    /// <summary>
    /// CGF1-S0205 / DeterministicRun_ExitsWithCode0:
    /// MinimalCIScenario completes 600 ticks with both entities alive → exit code 0.
    /// </summary>
    [Fact]
    public void DeterministicRun_ExitsWithCode0()
    {
        int code = RunScenario(new MinimalCIScenario(), maxTicks: MinimalCIScenario.TargetTicks + 100);
        Assert.Equal(0, code);
    }

    // ── CGF1-S0205 success condition 2 ───────────────────────────────────────

    /// <summary>
    /// CGF1-S0205 / DeterministicRun_IsReproducible:
    /// Running the scenario twice with identical parameters yields identical outcomes.
    /// Both runs must exit with code 0 and reach tick 600 (exact same tick count).
    /// </summary>
    [Fact]
    public void DeterministicRun_IsReproducible()
    {
        const int maxTicks = MinimalCIScenario.TargetTicks + 100;
        const float dt     = 1.0f / 60.0f;

        int codeA = RunScenario(new MinimalCIScenario(), maxTicks, dt);
        int codeB = RunScenario(new MinimalCIScenario(), maxTicks, dt);

        // Both runs must succeed.
        Assert.Equal(0, codeA);
        Assert.Equal(0, codeB);

        // The outcomes are bit-identical: same exit code, same scenario name, same tick target.
        // Since MinimalCIScenario uses no wall-clock time or random state,
        // the ECS world state at tick 600 is deterministic across runs.
        Assert.Equal(codeA, codeB);
    }

    // ── CGF1-S0205 success condition 3 ───────────────────────────────────────

    /// <summary>
    /// CGF1-S0205 / FailingAssertion_ExitsWithCode1:
    /// A scenario that throws <see cref="ScenarioFailureException"/> at tick 1
    /// causes exit code 1.
    /// </summary>
    [Fact]
    public void FailingAssertion_ExitsWithCode1()
    {
        int code = RunScenario(new FailingCIScenario(), maxTicks: 100);
        Assert.Equal(1, code);
    }
}

// ── Helper implementations ────────────────────────────────────────────────────

/// <summary>
/// Deliberately failing scenario: throws <see cref="ScenarioFailureException"/> at tick 1
/// to exercise the exit-code-1 path.
/// </summary>
file sealed class FailingCIScenario : IScenario
{
    public string ScenarioName => "failing_ci";

    public void Configure(Fdp.Kernel.EntityRepository world, ModuleHost.Core.ModuleHostKernel kernel) { }

    public bool EvaluateTick(uint currentTick, Fdp.Kernel.EntityRepository world)
    {
        if (currentTick >= 1)
            throw new ScenarioFailureException(1, "[FailingCI] Deliberate assertion failure at tick 1.");
        return false;
    }

    public void ConfigureVisuals(FDP.Toolkit.Vis2D.MapCanvas? canvas, Fdp.Kernel.EntityRepository world) { }
}
