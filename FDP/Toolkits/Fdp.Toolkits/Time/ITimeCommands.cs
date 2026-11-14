namespace Fdp.Toolkit.Time
{
    /// <summary>
    /// The ONE write surface for time control (`T4`), the counterpart to <see cref="ISimClock"/>.
    ///
    /// <para>Four control paths grew independently. Path A — the cluster path — publishes intents on
    /// the node's bus, the master drains them and fans the change out over DDS with a wall-clock
    /// barrier, so every node stops on the same tick. Paths B (the editor toolbar), C (the debugger)
    /// and D (the BTree/HSM tracer) instead call <c>SwitchToDeterministic</c> directly on the
    /// editor's own controller: no intent, no bus, no wire, and nothing outside that process ever
    /// learns the simulation stopped.</para>
    ///
    /// <para>This interface exists so B, C and D become A. It is deliberately INTENT-ONLY: an
    /// implementation publishes and returns, it does not reach into a controller. That is what makes
    /// the cluster-wide debugger pause fall out for free later — the intent already fans out, so the
    /// same call works whether the master is in this process or on the orchestrator.</para>
    /// </summary>
    public interface ITimeCommands
    {
        /// <summary>Requests that simulation time stop advancing.</summary>
        void Pause();

        /// <summary>Requests that simulation time resume advancing.</summary>
        void Resume();

        /// <summary>
        /// Requests exactly one deterministic step. Only meaningful while paused; the controller
        /// decides what a step is worth and whether it must wait for slave ACKs first.
        /// </summary>
        void StepOneTick();

        /// <summary>
        /// Requests one deterministic step of <paramref name="seconds"/> instead of the configured tick — the
        /// film director's coarser rates (1/20 s, 1/15 s) when 60 Hz cannot keep up in real time
        /// (`DESIGN_Film_Director.md` D-1). The master already honours the intent's delta.
        /// </summary>
        void StepBy(float seconds);

        /// <summary>Requests a new speed multiplier. Independent of whether time is advancing.</summary>
        void SetTimeScale(float scale);

        /// <summary>
        /// Requests that simulation time JUMP to <paramref name="position"/> and pause — the one discontinuity (a replay
        /// seek, a live branch, a scenario load). The caller knows why; the clock only knows where.
        /// </summary>
        void SnapTo(Fdp.Core.GlobalTime position);

        /// <summary>
        /// ⭐ <c>CE-3329</c> P1 (R4) — a RUNNING clock jumps to <paramref name="totalTime"/> and keeps running: no pause, no timeline-epoch
        /// move (so the kernel resets nothing). The replay's sim axis skips a paused stretch with it. A paused clock ignores it.
        /// </summary>
        void JumpTo(double totalTime) => throw new System.NotSupportedException($"{GetType().Name} cannot jump a running clock");
    }
}
