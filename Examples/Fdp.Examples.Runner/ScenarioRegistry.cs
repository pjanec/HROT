using Fdp.Examples.Common;
using Fdp.Examples.Common.Constants;

namespace Fdp.Examples.Runner
{
    /// <summary>
    /// Maps scenario name strings to <see cref="IScenario"/> factory functions.
    /// Registration is explicit (no reflection) to keep startup fast and errors obvious.
    /// <para>
    /// Phase 0: only the <c>placeholder</c> sentinel is registered. Concrete scenarios are
    /// added in later batches as they are implemented in <c>Fdp.Examples.Scenarios</c>.
    /// </para>
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

            // ── Phase 2+ scenarios (populated in future batches) ──────────────
            // ScenarioNames.AutoDrive       => new AutoDriveScenario(),
            // ScenarioNames.ComponentDamage => new ComponentDamageScenario(),
            // ...

            _ => throw new ArgumentException($"Unknown scenario: '{name}'. " +
                 $"Check {nameof(ScenarioNames)} for valid keys.", nameof(name))
        };
    }
}
