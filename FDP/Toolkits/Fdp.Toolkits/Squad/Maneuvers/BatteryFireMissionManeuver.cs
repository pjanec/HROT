using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// ⭐ <c>CE-2176</c> IF-2 (FM-1, R-2018) — a battery's FIRE MISSION: every gun that reaches puts its rounds on its aim point (the sheaf),
    /// timed so the first rounds land together when asked (time on target). One firing phase; the play ends it when every gun's rounds
    /// are gone. The adjust phase (one gun, the observer's corrections) is IF-3. 📄 docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.4.
    /// </summary>
    public static class BatteryFireMissionManeuver
    {
        public const ushort ManeuverKind = 14;

        public const ushort PhaseFiring = 0;
        public const ushort PhaseAborted = 1;

        public const byte ElementAll = 0;
        /// <summary>A gun firing its rounds.</summary>
        public const byte RoleFiring = 1;
        /// <summary>A gun that cannot reach, or has fired all its rounds.</summary>
        public const byte RoleStandby = 2;

        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new[]
            {
                new PhaseTransitionEntry { FromPhaseId = PhaseFiring, EventKind = PhaseEventKind.Abort, ToPhaseId = PhaseAborted },
            };
    }
}
