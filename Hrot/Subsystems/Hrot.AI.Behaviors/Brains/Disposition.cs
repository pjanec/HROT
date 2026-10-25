using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2376</c> (AQ104 C, R-2079) — what a man does about an enemy he cannot see: wait for him (Hold) or go and find him
    /// (Search); Unset when he has no character authored (today's behaviour).</summary>
    public enum DispositionKind : byte { Unset = 0, Hold = 1, Search = 2 }

    /// <summary>
    /// ⭐⭐ <c>CE-2376</c> (AQ104 C, R-2079; Temperament §7.6 DS-1) — the ONE reader both hunts ask. AQ89's <see cref="TemperamentOf.Willing"/>
    /// ⇒ Search, else Hold; nervous (stress above the band) ⇒ Hold; a man with NO <see cref="CombatTemperament"/> ⇒ Unset (TS-5's neutral
    /// rule: a type without character behaves as before). A Veteran or better who holds searches after <see cref="LongQuietSeconds"/>.
    /// 📄 docs/DESIGN_Temperament_And_Stress.md §7.6.
    /// </summary>
    public static class Disposition
    {
        /// <summary>A holding Veteran+ goes looking after this long with no fresh sighting (s).</summary>
        public const double LongQuietSeconds = 60.0;

        public static DispositionKind Of(EntityRepository world, Entity man, bool orderedAssault)
        {
            if (Stresses.Nervousness(world, man) > 0f) return DispositionKind.Hold;   // a frightened man does not go hunting
            if (!world.IsComponentTypeRegistered<CombatTemperament>() || !world.HasComponent<CombatTemperament>(man)) return DispositionKind.Unset;
            return TemperamentOf.Willing(world, man, orderedAssault) ? DispositionKind.Search : DispositionKind.Hold;
        }

        /// <summary>Sim seconds since <paramref name="man"/>'s memory last placed <paramref name="target"/> (∞ when he does not remember him).</summary>
        public static unsafe double QuietSeconds(EntityRepository world, Entity man, Entity target)
        {
            if (!world.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(man)) return double.PositiveInfinity;
            ref readonly var mem = ref world.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(man);
            long id = (long)target.PackedValue;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == id) return Ticks(world, mem.LastSeenTick[i]);
            return double.PositiveInfinity;
        }

        /// <summary>Sim seconds since a perception tick.</summary>
        public static double Ticks(EntityRepository world, uint tick)
        {
            uint now = Fdp.Toolkit.Perception.PerceptionClock.Now(world);
            if (tick >= now || !world.HasSingleton<GlobalTime>()) return 0d;
            var t = world.GetSingleton<GlobalTime>();
            double spf = t.FrameNumber > 0 && t.TotalTime > 0 ? t.TotalTime / t.FrameNumber : 1.0 / 60.0;
            return (now - tick) * spf;
        }

        /// <summary>He may go and look: not Hold — or Hold, a Veteran or better, and <paramref name="quietSeconds"/> since he last saw anything.</summary>
        public static bool MaySearch(EntityRepository world, Entity man, bool orderedAssault, double quietSeconds)
            => Of(world, man, orderedAssault) != DispositionKind.Hold
               || (quietSeconds >= LongQuietSeconds && ProficiencyOf.Tier(world, man) >= ProficiencyTier.Veteran && Stresses.Nervousness(world, man) <= 0f);
    }
}
