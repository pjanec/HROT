using System;

namespace Hrot.Core.Mission
{
    /// <summary>
    /// ⭐ <c>CE-4157</c> — a mission plan that cannot be run as authored: an unknown trigger type, a phase with more than one trigger, a
    /// non-empty trigger parameter that does not parse. ⛔ It used to fall back silently (a timer at 0 — the phase ended at once); the message
    /// names the task so the author finds it. A scenario load lets it fail the load, a runtime request is refused, a replica is not applied.
    /// </summary>
    public sealed class MissionPlanException : Exception
    {
        public MissionPlanException(string message) : base(message) { }
    }
}
