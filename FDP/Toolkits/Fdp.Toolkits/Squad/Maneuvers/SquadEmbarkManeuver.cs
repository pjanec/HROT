using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// ⭐ <c>CE-2173</c> slice 3 (B10, R-2015) — the squad BOARDS one vehicle: every member walks to it and gets in (a member carrying a
    /// casualty seats him first). One phase; the play ends it when every live member is aboard that vehicle — the mirror of
    /// <see cref="SquadDismountManeuver"/>. 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §9.
    /// </summary>
    public static class SquadEmbarkManeuver
    {
        public const ushort ManeuverKind = 13;

        public const ushort PhaseIn = 0;
        public const ushort PhaseAborted = 1;

        public const byte ElementAll = 0;
        /// <summary>A member told to board.</summary>
        public const byte RoleBoarding = 1;
        /// <summary>A member told to seat the casualty he carries, then board.</summary>
        public const byte RoleLoading = 2;

        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new[]
            {
                new PhaseTransitionEntry { FromPhaseId = PhaseIn, EventKind = PhaseEventKind.Abort, ToPhaseId = PhaseAborted },
            };
    }
}
