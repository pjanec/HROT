using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Events
{
    /// <summary>
    /// ⭐⭐ <c>CE-3364</c> (docs/DESIGN_Injury_States.md §ManDowned, docs/DESIGN_Scenario_Rules.md §00.13) — the ONE kill authority: a HIT took <see cref="Victim"/> out of the fight
    /// (Ok / Wounded → Incapacitated or Dead), raised ONCE by <c>HealthApplicationSystem</c> on the node that owns the victim's Health (the Brain — Health is in the Brain group, so
    /// the scenario measures read it on the same node). Never for a body a hit finds already down, never for a bleed-out (Incapacitated → Dead: he was already out).
    /// ⛔ No wire topic: a kill feed on other nodes would add one.
    /// </summary>
    [EventId(CombatConstants.ManDownedEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct ManDownedEvent
    {
        /// <summary>The man who went down.</summary>
        public Entity Victim;
        /// <summary>Who dealt the hit that did it (<see cref="DamageAssessedEvent.Source"/>); <see cref="Entity.Null"/> = unknown.</summary>
        public Entity Source;
        /// <summary>The first thrower of a rethrown round (<see cref="DamageAssessedEvent.OriginalSource"/>); <see cref="Entity.Null"/> = same as <see cref="Source"/>.</summary>
        public Entity OriginalSource;
        /// <summary>What the hit made of him: <see cref="InjuryState.Incapacitated"/> or <see cref="InjuryState.Dead"/>.</summary>
        public InjuryState State;
        /// <summary>1 when the killing hit landed on a body part (<see cref="BodyPart"/>).</summary>
        public byte HasBodyPart;
        /// <summary>The part id within the body plan.</summary>
        public byte BodyPart;
        /// <summary>The victim's force at that moment (<c>Neutral</c> when he has none).</summary>
        public ForceId VictimSide;
    }
}
