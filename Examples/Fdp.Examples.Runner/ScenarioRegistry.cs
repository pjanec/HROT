using Fdp.Examples.Common;
using Fdp.Examples.Common.Constants;
using Fdp.Examples.Scenarios.Kinematics;
using Fdp.Examples.Scenarios.Perception;
using Fdp.Examples.Scenarios.Physics;

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
            ScenarioNames.BallisticsAndHit => new BallisticsAndHitScenario(),
            ScenarioNames.SensorGrid       => new SensorGridScenario(),

            _ => throw new ArgumentException($"Unknown scenario: '{name}'. " +
                 $"Check {nameof(ScenarioNames)} for valid keys.", nameof(name))
        };
    }
}
