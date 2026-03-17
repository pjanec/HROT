namespace Fdp.Examples.Common
{
    /// <summary>
    /// Thrown by an <see cref="IScenario.EvaluateTick"/> implementation to signal a
    /// deterministic assertion failure. The <see cref="ScenarioSubsystem"/> catches this,
    /// logs the diagnostic message, and exits with code 1.
    /// </summary>
    public sealed class ScenarioFailureException : Exception
    {
        /// <summary>Phase number in which the failure occurred.</summary>
        public int PhaseId { get; }

        /// <summary>Human-readable diagnostic string (e.g. "Y=0.1 expected >2.0").</summary>
        public string Diagnostics { get; }

        /// <param name="phaseId">Phase index that failed.</param>
        /// <param name="message">Diagnostic message forwarded to the CI log.</param>
        public ScenarioFailureException(int phaseId, string message) : base(message)
        {
            PhaseId = phaseId;
            Diagnostics = message;
        }
    }
}
