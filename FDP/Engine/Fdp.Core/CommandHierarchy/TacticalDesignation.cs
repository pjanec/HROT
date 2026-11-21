namespace Fdp.Core.CommandHierarchy
{
    /// <summary>
    /// Logical role of a subordinate entity within its commander's unit.
    /// Zero value (<c>Undefined</c>) is the safe default for new entities with no commander.
    /// </summary>
    /// IMPORTANT: Must be kept in sync with Hrot.NED.Descriptors.eTacticalDesignation
    public enum TacticalDesignation : ushort
    {
        Undefined    = 0,
        Commander    = 1,
        SquadLeader  = 2,
        Wingman      = 3,
        Support      = 4,
        /// <summary>⭐ CE-2125 — a fire-team leader: first in line when the squad leader falls (§7.2 L1).</summary>
        TeamLeader   = 5,
        /// <summary>⭐ <c>CE-2152</c> C-c (R-2012 K3′) — the squad's medic: the one the commander sends for a man who is down. ⭐ On the NED
        /// wire enum <c>eTacticalDesignation</c> since <c>CE-3253</c> (BR-8), with <see cref="TeamLeader"/>.</summary>
        Medic        = 6,
        // ⭐ CE-3253 (BR-8; DA-19, R-2036 — docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §12) — the squad's ROLES, appended;
        //   readers (the element split by fire team, the MG first into the base of fire, role takeover) are behaviors' CE-2281.
        /// <summary>The fire team's automatic rifleman (an M249-class LMG).</summary>
        AutomaticRifleman = 7,
        /// <summary>A machine gunner (a PKM / M240-class gun) — the base of fire's first man.</summary>
        MachineGunner = 8,
        /// <summary>The fire team's grenadier.</summary>
        Grenadier    = 9,
        /// <summary>A marksman (DMR / sniper — BR-9).</summary>
        Marksman     = 10,
        /// <summary>An anti-tank gunner.</summary>
        AntiTank     = 11,
    }
}
