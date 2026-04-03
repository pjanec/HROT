namespace FDP.Toolkit.Orchestration
{
    /// <summary>
    /// Strongly-typed status codes used across all FDP orchestration domain events.
    ///
    /// <para>
    /// <b>Range design:</b>
    /// <list type="table">
    ///   <item><term>0–9</term><description>Lifecycle (0 = Success, 1 = InProgress, 2 = Pending)</description></item>
    ///   <item><term>10–99</term><description>Generic errors (Rejected, Timeout, Cancelled)</description></item>
    ///   <item><term>100–999</term><description>Federation errors</description></item>
    ///   <item><term>1000+</term><description>Node / slave errors</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Default-value guarantee:</b> <c>0</c> is the C# default for uninitialized
    /// fields, so a zero-initialised event struct naturally means "OK" — consistent
    /// with <c>NedStatusCode</c> already used in Hrot DDS messages.
    /// </para>
    /// </summary>
    public enum OrchestrationStatusCode : int
    {
        // ── Lifecycle (0–9) ────────────────────────────────────────────────────
        Success    = 0,
        InProgress = 1,
        Pending    = 2,

        // ── Generic errors (10–99) ─────────────────────────────────────────────
        Rejected  = 10,
        Timeout   = 11,
        Cancelled = 12,
        Failure   = 13,

        // ── Federation errors (100–999) ────────────────────────────────────────
        InvalidZone      = 101,
        ExerciseMismatch = 102,

        // ── Node / slave errors (1000+) ────────────────────────────────────────
        OutOfMemory   = 1000,
        AssetNotFound = 1001,
    }

    /// <summary>
    /// Extension methods for <see cref="OrchestrationStatusCode"/> and raw <c>int</c> wire values.
    /// </summary>
    public static class OrchestrationStatusCodeExtensions
    {
        /// <summary>
        /// Returns <c>true</c> when <paramref name="code"/> represents a terminal
        /// failure (i.e. any code ≥ 10).
        /// </summary>
        public static bool IsError(this OrchestrationStatusCode code) => (int)code >= 10;

        /// <summary>
        /// Returns <c>true</c> when <paramref name="code"/> represents a terminal
        /// failure (i.e. any code ≥ 10). Overload for raw DDS wire-format integers.
        /// </summary>
        public static bool IsError(this int code) => code >= 10;
    }
}
