using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-2238</c> part 3 (AW-22, R-2028; docs/DESIGN_Civilians_And_Crowd_Control.md §5h.6) — a civilian who commits a LETHAL hostile
    /// act is a KNOWN COMBATANT to the units that witnessed it and to their squad, sticky for the exercise; to everyone else he is still a
    /// civilian. Knowledge per OBSERVER, kept in his <see cref="TargetMemory"/> (KC-1), read by <see cref="Armament.Judge"/>.
    /// Brain-local: it runs where the brains and their memories are, never on the wire.
    /// </summary>
    public static unsafe class KnownCombatants
    {
        /// <summary>How many known combatants one unit keeps (full ⇒ the oldest goes).</summary>
        public const int Max = 8;

        /// <summary>How recently a witness must have perceived the actor (ticks, ~1 s at 60 Hz) — KC-2.</summary>
        public const uint WitnessWindowTicks = 60;

        /// <summary><paramref name="observer"/> now knows <paramref name="man"/> is a combatant. False when the observer has no memory.</summary>
        public static bool Mark(EntityRepository world, Entity observer, Entity man)
        {
            if (man.IsNull || !world.IsAlive(observer) || !world.IsComponentTypeRegistered<TargetMemory>()
                || !world.HasComponent<TargetMemory>(observer)) return false;
            ref var mem = ref world.GetComponentRW<TargetMemory>(observer);
            long id = (long)man.PackedValue;
            for (int i = 0; i < mem.KnownCombatantCount; i++)
                if (mem.KnownCombatantIds[i] == id) return true;
            if (mem.KnownCombatantCount == Max)
            {
                for (int i = 1; i < Max; i++) mem.KnownCombatantIds[i - 1] = mem.KnownCombatantIds[i];   // the oldest goes
                mem.KnownCombatantCount--;
            }
            mem.KnownCombatantIds[mem.KnownCombatantCount++] = id;
            return true;
        }

        /// <summary>KC-2 — every unit on this node whose memory holds <paramref name="actor"/> IDENTIFIED and perceived within
        /// <see cref="WitnessWindowTicks"/> learns he is a combatant. Returns how many witnesses.</summary>
        public static int MarkWitnesses(EntityRepository world, Entity actor)
        {
            if (actor.IsNull || !world.IsComponentTypeRegistered<TargetMemory>()) return 0;
            long id = (long)actor.PackedValue;
            uint now = world.SimulationTick;   // what ThreatEvaluationSystem stamps LastSeenTick with (ISimulationView.Tick)
            int n = 0;
            foreach (var e in world.Query().With<TargetMemory>().Build())
            {
                if (e == actor) continue;
                ref readonly var mem = ref world.GetComponentRO<TargetMemory>(e);
                bool witnessed = false;
                for (int k = 0; k < mem.Count && !witnessed; k++)
                    witnessed = mem.EntityIds[k] == id && !TargetMemory.IsAnonymous(in mem, k) && now - mem.LastSeenTick[k] <= WitnessWindowTicks;
                if (witnessed && Mark(world, e, actor)) n++;
            }
            return n;
        }

        /// <summary>KC-3 — does <paramref name="observer"/> know <paramref name="man"/> is a combatant: his own list, his commander's, or
        /// any squad-mate's (<see cref="UnitSubordinate.Commander"/> → <see cref="UnitRoster"/>)?</summary>
        public static bool Knows(ISimulationView view, Entity observer, Entity man)
        {
            if (man.IsNull || observer.IsNull || view is not EntityRepository world || !world.IsComponentTypeRegistered<TargetMemory>()) return false;
            long id = (long)man.PackedValue;
            if (Holds(world, observer, id)) return true;
            if (!world.IsComponentTypeRegistered<UnitSubordinate>() || !world.HasComponent<UnitSubordinate>(observer)) return false;
            var commander = world.GetComponentRO<UnitSubordinate>(observer).Commander;
            if (commander.IsNull || !world.IsAlive(commander)) return false;
            if (Holds(world, commander, id)) return true;
            if (!world.IsComponentTypeRegistered<UnitRoster>() || !world.HasComponent<UnitRoster>(commander)) return false;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(commander);
            for (int m = 0; m < roster.Count; m++)
            {
                var mate = roster.SubordinateEntities[m];
                if (mate != observer && Holds(world, mate, id)) return true;
            }
            return false;
        }

        private static bool Holds(EntityRepository world, Entity unit, long id)
        {
            if (!world.IsAlive(unit) || !world.HasComponent<TargetMemory>(unit)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(unit);
            for (int i = 0; i < mem.KnownCombatantCount; i++)
                if (mem.KnownCombatantIds[i] == id) return true;
            return false;
        }
    }
}
