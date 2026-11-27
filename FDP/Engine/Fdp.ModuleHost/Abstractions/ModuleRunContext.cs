using System;

namespace Fdp.ModuleHost.Abstractions
{
    /// <summary>
    /// ⭐⭐ <c>CE-11076</c> — what the module host tells the run it is executing: how much WALL time it has, and which timeline it belongs to.
    /// A cooperative module (the path solver) reads it to bound its own work, answer the rest on its next run, and never reach the host's
    /// timeout (an abandoned run's output is discarded, <c>Fdp.ModuleHost.md</c> §5).
    /// <para>⛔ <see cref="WallBudgetMs"/> is 0 on a STEPPED frame (R-3035: a stepped run is waited for whole and never depends on the wall
    /// clock), on a synchronous dispatch and outside any run (tests, a host that never sets it) ⇒ 0 means "do everything", the old behaviour.</para>
    /// <para>Thread-static: the host sets it on the run's own thread for the duration of the run (<see cref="Enter"/>).</para>
    /// </summary>
    public static class ModuleRunContext
    {
        /// <summary>The share of the module's <c>MaxExpectedRuntimeMs</c> a live background run is given as its own budget — the rest is headroom
        /// for the one unit of work that starts just before the budget runs out.</summary>
        public const double BudgetShare = 0.5;

        [ThreadStatic] private static double t_wallBudgetMs;
        [ThreadStatic] private static int t_timelineEpoch;

        /// <summary>Wall milliseconds this run may spend before it should stop starting new work; 0 = no budget (do everything).</summary>
        public static double WallBudgetMs => t_wallBudgetMs;

        /// <summary>The host's timeline epoch at the run's dispatch (+1 whenever sim time goes back: a load, a preview Stop). Work carried
        /// from a run of an older epoch is about a world that no longer exists.</summary>
        public static int TimelineEpoch => t_timelineEpoch;

        /// <summary>Sets the context for the current thread until the returned scope is disposed (then the previous values come back).</summary>
        public static Scope Enter(double wallBudgetMs, int timelineEpoch)
        {
            var scope = new Scope(t_wallBudgetMs, t_timelineEpoch);
            t_wallBudgetMs = wallBudgetMs < 0 ? 0 : wallBudgetMs;
            t_timelineEpoch = timelineEpoch;
            return scope;
        }

        /// <summary>Restores the previous context (a struct: no allocation per run).</summary>
        public readonly struct Scope : IDisposable
        {
            private readonly double _budget;
            private readonly int _epoch;
            internal Scope(double budget, int epoch) { _budget = budget; _epoch = epoch; }
            public void Dispose() { t_wallBudgetMs = _budget; t_timelineEpoch = _epoch; }
        }
    }
}
