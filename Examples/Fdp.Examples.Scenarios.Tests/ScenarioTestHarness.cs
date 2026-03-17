using Fdp.Examples.Common;
using FDP.Framework.Runner;

namespace Fdp.Examples.Scenarios.Tests
{
    /// <summary>
    /// Static helper that runs an <see cref="IScenario"/> through a headless, deterministic
    /// <see cref="ScenarioSubsystem"/> without calling <see cref="Environment.Exit"/>.
    /// Returns the captured exit code: 0 = success, 1 = assertion failed, 2 = timeout.
    /// </summary>
    public static class ScenarioTestHarness
    {
        /// <summary>
        /// Runs <paramref name="scenario"/> in headless + deterministic mode.
        /// </summary>
        /// <param name="scenario">The scenario implementation to execute.</param>
        /// <param name="maxTicks">Tick budget — returns 2 if this is exceeded.</param>
        /// <param name="dt">Fixed simulation delta in seconds (default 1/60 s).</param>
        /// <returns>0 on success, 1 on assertion failure, 2 on timeout.</returns>
        public static int Run(IScenario scenario, int maxTicks = 500, float dt = 1.0f / 60.0f)
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

            var orch = new SubsystemOrchestrator(new[] { sub }, opts);
            sub.AttachOrchestrator(orch);

            orch.Initialize();
            orch.Run();      // Blocks until sub calls orch.Stop() via exit callback.
            orch.Shutdown();

            return capturedCode;
        }
    }
}
