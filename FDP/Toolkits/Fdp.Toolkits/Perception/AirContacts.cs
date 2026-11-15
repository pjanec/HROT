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
            // ⭐ airborne insertion B-6 (D-4) — an AIRCRAFT: nobody shelters from a paratrooper, and no SAM is cued at one
            // ⭐ CE-2436 AU-5 (behaviors lane, FDP file — said) — an aircraft ON THE GROUND (a drone parked beside its operator, a helicopter
            //   on its pad) is no air threat: live (cc-battalion runs #4, #5) Red's outpost sheltered at t = 0 from Blue's parked drones —
            //   which still read Airborne 1 (the translator's default; only a flying order lands them), hence FlightGround's second test.
            return view is EntityRepository repo && repo.IsAlive(e) && Fdp.Toolkit.Motion.Locomotions.IsAircraft(repo, e)
                   && !Fdp.Toolkit.Motion.FlightGround.OnTheGround(repo, e);
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
