using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;

namespace Fdp.Toolkit.Perception
{
    /// <summary>
    /// ⭐ <c>CE-2165</c> V8 (DESIGN_Vehicle_Exposure_Cycle §8) — the ONE rule for "this memory slot is an AIR contact": an identified one
    /// whose body moves in the Air domain (its <c>Locomotion</c>, stamped on every node — a drone, a helicopter), or a heard one of class
    /// <c>Rotor</c>. Read by <c>ThreatEvaluationSystem</c> (the <c>AirThreat</c> edge) and by the behaviours that react to it.
    /// </summary>
    public static class AirContacts
    {
        public static unsafe bool IsAir(ISimulationView view, in TargetMemory mem, int slot)
        {
            if (TargetMemory.IsAnonymous(in mem, slot)) return mem.SourceClass[slot] == (byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.Rotor;
            var e = new Entity((ulong)mem.EntityIds[slot]);
            return view is EntityRepository repo && repo.IsAlive(e)
                && Fdp.Toolkit.Motion.Locomotions.DomainOf(Fdp.Toolkit.Motion.Locomotions.Of(repo, e)) == Fdp.Toolkit.Motion.MotionDomain.Air;
        }

        /// <summary>How many air contacts <paramref name="mem"/> holds.</summary>
        public static int Count(ISimulationView view, in TargetMemory mem)
        {
            int n = 0;
            for (int i = 0; i < mem.Count; i++) if (IsAir(view, in mem, i)) n++;
            return n;
        }
    }
}
