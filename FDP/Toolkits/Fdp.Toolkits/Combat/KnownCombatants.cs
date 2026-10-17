using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-2238</c> part 4 (AW-22; docs/DESIGN_Civilians_And_Crowd_Control.md §5h.6 KC-1, as revised) — the civilians a unit SAW commit a
    /// lethal act (packed entity ids; full ⇒ the oldest goes). Brain-local, never saved, ONE writer: <see cref="KnownCombatants"/>.
    /// ⛔ Was a list inside <see cref="TargetMemory"/>: the <c>ThreatEvaluationSystem</c> copies the memory and writes the copy back through
    /// the command buffer, so a mark made later in the same frame was erased — measured on <c>ua-armed-blend-back</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.KnownCombatantList)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct KnownCombatantList
    {
        public fixed long Ids[KnownCombatants.Max];
        public byte Count;
    }

    /// <summary>
    /// ⭐ <c>CE-2238</c> part 3 (AW-22, R-2028; docs/DESIGN_Civilians_And_Crowd_Control.md §5h.6) — a civilian who commits a LETHAL hostile
    /// act is a KNOWN COMBATANT to the units that witnessed it and to their squad, sticky for the exercise; to everyone else he is still a
    /// civilian. Knowledge per OBSERVER, kept in his <see cref="KnownCombatantList"/>, read by <see cref="Armament.Judge"/>.
    /// Brain-local: it runs where the brains and their memories are, never on the wire.
    /// </summary>
    public static unsafe class KnownCombatants
    {
        /// <summary>How many known combatants one unit keeps (full ⇒ the oldest goes).</summary>
        public const int Max = 8;

        /// <summary>How recently a witness must have perceived the actor (ticks, ~1 s at 60 Hz) — KC-2.</summary>
        public const uint WitnessWindowTicks = 60;

        [System.ThreadStatic] private static System.Collections.Generic.List<Entity>? _witnesses;

        /// <summary><paramref name="observer"/> now knows <paramref name="man"/> is a combatant. False when the observer has no memory.</summary>
        public static bool Mark(EntityRepository world, Entity observer, Entity man)
        {
            if (man.IsNull || !world.IsAlive(observer) || !world.IsComponentTypeRegistered<TargetMemory>()
                || !world.HasComponent<TargetMemory>(observer)) return false;
            if (!world.IsComponentTypeRegistered<KnownCombatantList>()) world.RegisterComponent<KnownCombatantList>();
            if (!world.HasComponent<KnownCombatantList>(observer)) world.AddComponent(observer, new KnownCombatantList());
            ref var list = ref world.GetComponentRW<KnownCombatantList>(observer);
            long id = (long)man.PackedValue;
            for (int i = 0; i < list.Count; i++)
                if (list.Ids[i] == id) return true;
            if (list.Count == Max)
            {
                for (int i = 1; i < Max; i++) list.Ids[i - 1] = list.Ids[i];   // the oldest goes
                list.Count--;
            }
            list.Ids[list.Count++] = id;
            return true;
        }

        /// <summary>KC-2 — every unit on this node whose memory holds <paramref name="actor"/> IDENTIFIED and perceived within
        /// <see cref="WitnessWindowTicks"/> witnessed his act: each is <see cref="Mark"/>ed. Returns how many.</summary>
        public static int MarkWitnesses(EntityRepository world, Entity actor)
        {
            if (actor.IsNull || !world.IsComponentTypeRegistered<TargetMemory>()) return 0;
            long id = (long)actor.PackedValue;
            uint now = world.SimulationTick;   // what ThreatEvaluationSystem stamps LastSeenTick with (ISimulationView.Tick)
            var witnesses = _witnesses ??= new System.Collections.Generic.List<Entity>();
            witnesses.Clear();
            foreach (var e in world.Query().With<TargetMemory>().Build())
            {
                if (e == actor) continue;
                ref readonly var mem = ref world.GetComponentRO<TargetMemory>(e);
                for (int k = 0; k < mem.Count; k++)
                    if (mem.EntityIds[k] == id && !TargetMemory.IsAnonymous(in mem, k) && now - mem.LastSeenTick[k] <= WitnessWindowTicks)
                    { witnesses.Add(e); break; }
            }
            int n = 0;
            foreach (var e in witnesses) if (Mark(world, e, actor)) n++;   // ⭐ after the walk: Mark may add the list (a structural change)
            return n;
        }

        /// <summary>KC-3 — does <paramref name="observer"/> know <paramref name="man"/> is a combatant: his own list, his commander's, or
        /// any squad-mate's (<see cref="UnitSubordinate.Commander"/> → <see cref="UnitRoster"/>; a commander reads his own roster)?</summary>
        public static bool Knows(ISimulationView view, Entity observer, Entity man)
        {
            if (man.IsNull || observer.IsNull || view is not EntityRepository world || !world.IsComponentTypeRegistered<TargetMemory>()) return false;
            long id = (long)man.PackedValue;
            if (Holds(world, observer, id)) return true;
            // his squad: the commander he answers to — or, when he IS a commander, his own roster
            var commander = world.IsComponentTypeRegistered<UnitSubordinate>() && world.HasComponent<UnitSubordinate>(observer)
                ? world.GetComponentRO<UnitSubordinate>(observer).Commander : observer;
            if (commander.IsNull || !world.IsAlive(commander)) return false;
            if (commander != observer && Holds(world, commander, id)) return true;
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
            if (!world.IsAlive(unit) || !world.IsComponentTypeRegistered<KnownCombatantList>() || !world.HasComponent<KnownCombatantList>(unit)) return false;
            ref readonly var list = ref world.GetComponentRO<KnownCombatantList>(unit);
            for (int i = 0; i < list.Count; i++)
                if (list.Ids[i] == id) return true;
            return false;
        }
    }
}
