namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐⭐ <c>CE-4038</c> B4 (docs/DESIGN_Film_Director.md D-11) — "is the scenario I asked for loaded?", EXTRACTED from
    /// <c>DebugApiHost.HandleScenarioLoad</c> so the route and the <see cref="DirectorRunner"/> share one measured answer.
    ///
    /// <para>⭐⭐⭐ <b>READINESS IS AN EDGE, NOT A LEVEL</b> (measured <c>2026-08-24</c>, see <c>DebugApiService.WorldEntityCount</c>): the
    /// target state is where a RELOAD starts, so a level check answers "ready" while the PREVIOUS world still stands. ⇒ ready = the state is the
    /// target AND the world's entity count was seen to CHANGE since the request (a load clears and re-creates) AND it has settled (the same
    /// count two polls running).</para>
    ///
    /// <para>⚠ The grace fallback covers the one case the edge cannot see — a scenario whose count never differs from the current one at any
    /// polled frame (empty → empty): after <see cref="EdgeGraceSeconds"/> the change is not required. ⛔ It relaxes the edge, never the state.
    /// ⚠ A host with no world (no count) has no edge to watch: the state is all there is, reported through <see cref="HadWorldAnchor"/>.</para>
    ///
    /// <para>One probe per load: arm it with the count from BEFORE the request, then <see cref="Observe"/> once per frame.</para>
    /// </summary>
    public sealed class ScenarioReadyProbe
    {
        public const double EdgeGraceSeconds = 2.0;

        private readonly int? _countAtRequest;
        private int? _previous;

        /// <param name="countAtRequest">The world's entity count when the load was requested (null: the host has no world).</param>
        public ScenarioReadyProbe(int? countAtRequest)
        {
            _countAtRequest = countAtRequest;
            _previous = countAtRequest;
        }

        /// <summary>Whether the world was seen to change since the request (false when only the grace let it pass).</summary>
        public bool SawWorldChange { get; private set; }

        /// <summary>Whether there was a count to watch at all.</summary>
        public bool HadWorldAnchor => _countAtRequest is not null;

        public int Polls { get; private set; }

        /// <summary>The last count seen.</summary>
        public int? LastCount => _previous;

        /// <summary>One poll: <paramref name="stateIsTarget"/> (the cluster is in the load's target state), the world's count now, and the
        /// seconds since the request. True once the load is ready.</summary>
        public bool Observe(bool stateIsTarget, int? count, double elapsedSeconds)
        {
            Polls++;
            if (count != _countAtRequest) SawWorldChange = true;
            bool settled = count == _previous;
            _previous = count;
            return stateIsTarget && settled && (SawWorldChange || !HadWorldAnchor || elapsedSeconds > EdgeGraceSeconds);
        }
    }
}
