using Fdp.Examples.Common;
using Fdp.Examples.Common.Constants;
using Fdp.Examples.Scenarios.Cognitive;
using Fdp.Examples.Scenarios.Integrated;
using Fdp.Examples.Scenarios.Kinematics;
using Fdp.Examples.Scenarios.Network;
using Fdp.Examples.Scenarios.Perception;
using Fdp.Examples.Scenarios.Physics;
using Fdp.Examples.Scenarios.Replay;

namespace Fdp.Examples.Runner
{
    /// <summary>
    /// Maps scenario name strings to <see cref="IScenario"/> factory functions.
    /// Registration is explicit (no reflection) to keep startup fast and errors obvious.
    /// </summary>
    public static class ScenarioRegistry
    {
        /// <summary>
        /// Creates and returns an <see cref="IScenario"/> instance for the given name.
        /// </summary>
        /// <param name="name">Scenario key (case-insensitive, matches <see cref="ScenarioNames"/> constants).</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is not registered.</exception>
        public static IScenario Create(string name) => name.ToLowerInvariant() switch
        {
            // ── Phase 0 placeholder ───────────────────────────────────────────
            "placeholder" => new PlaceholderScenario(),

            // ── Phase 2 demos (BATCH-03) ──────────────────────────────────────
            ScenarioNames.AutoDrive       => new AutoDriveScenario(),
            ScenarioNames.ComponentDamage => new ComponentDamageScenario(),

            // ── Phase 3 demos (BATCH-04 / BATCH-05) ───────────────────────────
            ScenarioNames.BallisticsAndHit     => new BallisticsAndHitScenario(),
            ScenarioNames.BehaviorValidation   => new BehaviorValidationScenario(),
            ScenarioNames.SensorGrid           => new SensorGridScenario(),

            // ── Phase 4 demos (BATCH-06) ──────────────────────────────────────
            ScenarioNames.MissionCommand  => new MissionCommandScenario(),
            ScenarioNames.TerrainClamping => new TerrainClampingScenario(),

            // ── Phase 4 demos (BATCH-07) ──────────────────────────────────────
            ScenarioNames.ParallelStories => new ParallelStoriesScenario(),

            // ── Phase 5 demos (BATCH-09) ──────────────────────────────────────
            ScenarioNames.DistributedTank => new DistributedTankScenario(),

            // ── Phase 6 demos (BATCH-15) ──────────────────────────────────────
            ScenarioNames.UrbanCombat => new UrbanCombatNewScenario(),

            _ => throw new ArgumentException($"Unknown scenario: '{name}'. " +
                 $"Check {nameof(ScenarioNames)} for valid keys.", nameof(name))
        };
    }
}
