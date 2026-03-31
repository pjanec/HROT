using Fdp.Examples.Common;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D;
using ModuleHost.Core;

namespace Hrot.ClusterRunner.Scenarios;

/// <summary>
/// Minimal deterministic CI scenario used to validate the headless <c>--mode ci</c> path.
///
/// <para>Lifecycle:
/// <list type="number">
///   <item>Spawns two dummy entities.</item>
///   <item>Asserts both entities remain alive at every tick.</item>
///   <item>Succeeds (returns <c>true</c>) after 600 deterministic ticks (10 s at 60 Hz).</item>
/// </list>
/// </para>
/// <para>Exit codes: 0 = both entities alive after 600 ticks, 1 = entity death detected.</para>
/// </summary>
internal sealed class MinimalCIScenario : IScenario
{
    // ── Constants ─────────────────────────────────────────────────────────
    /// <summary>Public scenario key used by <c>--scenario MinimalCI_01</c>.</summary>
    public const string Key = "minimalci_01";

    /// <summary>Target tick count: 600 = 10 s at 60 Hz.</summary>
    public const int TargetTicks = 600;

    // ── State ─────────────────────────────────────────────────────────────
    private Entity _e1;
    private Entity _e2;

    /// <summary>
    /// Structural snapshot captured at <see cref="TargetTicks"/>.
    /// Used by <c>DeterministicRun_IsReproducible</c> to assert bit-identical entity IDs
    /// between two independent runs of the same scenario with the same configuration.
    /// </summary>
    internal (Entity E1, Entity E2) FinalEntitySnapshot { get; private set; }

    // ── IScenario ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string ScenarioName => Key;

    /// <inheritdoc/>
    public void Configure(EntityRepository world, ModuleHostKernel kernel)
    {
        // Spawn two dummy entities — no components needed; isAlive check is sufficient
        // to verify the ECS world state survives 600 deterministic frames.
        _e1 = world.CreateEntity();
        _e2 = world.CreateEntity();
    }

    /// <inheritdoc/>
    public bool EvaluateTick(uint currentTick, EntityRepository world)
    {
        if (!world.IsAlive(_e1))
            throw new ScenarioFailureException(1, $"[MinimalCI] Entity 1 (id={_e1}) is no longer alive at tick {currentTick}.");

        if (!world.IsAlive(_e2))
            throw new ScenarioFailureException(1, $"[MinimalCI] Entity 2 (id={_e2}) is no longer alive at tick {currentTick}.");

        if (currentTick >= TargetTicks)
        {
            // Capture entity IDs for reproducibility assertion.
            FinalEntitySnapshot = (_e1, _e2);
            return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }
}
