namespace FDP.Toolkit.Orchestration
{
    /// <summary>
    /// Named constants for the unified <see cref="NodeOpCompletedEvent"/> status code
    /// scheme used across all FDP orchestration messages.
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
    /// <c>int</c> fields, so a zero-initialised wire struct naturally means "OK" —
    /// consistent with <c>NedStatusCode</c> already used in Hrot DDS messages.
    /// </para>
    /// </summary>
    public static class OrchestrationStatusCode
    {
        // ── Lifecycle (0–9) ────────────────────────────────────────────────────
        public const int Success    = 0;
        public const int InProgress = 1;
        public const int Pending    = 2;

        // ── Generic errors (10–99) ─────────────────────────────────────────────
        public const int Rejected  = 10;
        public const int Timeout   = 11;
        public const int Cancelled = 12;
        public const int Failure   = 13;

        // ── Federation errors (100–999) ────────────────────────────────────────
        public const int InvalidZone      = 101;
        public const int ExerciseMismatch = 102;

        // ── Node / slave errors (1000+) ────────────────────────────────────────
        public const int OutOfMemory   = 1000;
        public const int AssetNotFound = 1001;

        /// <summary>
        /// Returns <c>true</c> when <paramref name="code"/> represents a terminal
        /// failure (i.e. any code ≥ 10).
        /// </summary>
        public static bool IsError(int code) => code >= 10;
    }
}
