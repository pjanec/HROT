using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception;

namespace Fdp.Toolkit.Squad
{
    /// <summary>
    /// ⭐⭐ <c>CE-2353</c> — THE ONE "is this pooled contact an enemy?" for every squad reader: a HEARD contact is (there is no entity to
    /// read — a burst is never pooled, R-2071); an identified one is when he is alive, still in the fight
    /// (<see cref="ThreatDanger.IsOutOfTheFight"/>: not killed, down, surrendered or in custody) and not NEUTRAL
    /// (<see cref="ThreatDanger.IsNeutral"/>, CV-8: a neutral is no enemy; a covert man stays Neutral until he acts).
    /// <para>Readers: the SPOT (<c>StaffSystems.ComposeSpot</c> — the platoon's picture), the flank sensor, the halted-carrier dismount
    /// row, the reaction's "nothing to fight" and its hold-until-clear. 📌 Live on <c>uc-urban-battle</c> (<c>2026-10-22</c>) these had
    /// drifted into four different tests: the platoon's picture was empty while two civilians in the pool kept a squad's reaction
    /// holding for 30 minutes and put every remounting squad out again.</para>
    /// </summary>
    public static class SquadContacts
    {
        public static bool IsEnemy(ISimulationView view, in SquadContact c)
        {
            if ((c.Flags & SquadContact.AnonymousFlag) != 0 || c.EntityId <= 0) return true;
            var e = new Entity((ulong)c.EntityId);
            return view.IsAlive(e) && !ThreatDanger.IsOutOfTheFight(view, e) && !ThreatDanger.IsNeutral(view, e);
        }

        /// <summary>The squad's pool holds at least one <see cref="IsEnemy"/> contact.</summary>
        public static bool AnyEnemy(ISimulationView view, in SquadContactPool pool)
        {
            var span = Slots(in pool);
            for (int i = 0; i < pool.Count && i < span.Length; i++)
                if (IsEnemy(view, in span[i])) return true;
            return false;
        }

        /// <summary>The pool's live slots as a span (16 at most).</summary>
        public static ReadOnlySpan<SquadContact> Slots(in SquadContactPool pool)
            => MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref Unsafe.AsRef(in pool.Contacts)), 16);
    }
}
