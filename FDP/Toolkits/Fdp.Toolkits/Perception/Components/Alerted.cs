using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Components
{
    /// <summary>
    /// ⭐⭐ <c>CE-2371</c> (AQ104 D, R-2079, behaviors) — the enemies a man must NOT forget: up to four memory ids, each held by a short LEASE
    /// his task renews while it wants him alert (an attack order on that man; a defence or a clearing of the area he is in). While a lease
    /// runs, <c>ThreatEvaluationSystem</c> never forgets the slot and the hunt reads it as live. 🔒 User: <i>"they should stay alerted until
    /// the enemy is incapacitated"</i>. Brain-local (NoScenario, never sent). Written and read only through <see cref="Alerts"/>.
    /// 📄 docs/blueprints/Architect_Question_104_Area_And_Building_Combat.md §4 D.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Alerted)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Alerted
    {
        public long Id0, Id1, Id2, Id3;
        /// <summary>Sim time (s) each lease runs out; a slot whose time has passed is free.</summary>
        public double Until0, Until1, Until2, Until3;
    }

    /// <summary>⭐ <c>CE-2371</c> — the one door to <see cref="Alerted"/>.</summary>
    public static class Alerts
    {
        /// <summary>How long one renewal holds (s): a task that stops renewing lets the man forget normally a moment later.</summary>
        public const float LeaseSeconds = 2f;

        /// <summary>Keeps <paramref name="memoryId"/> pinned on <paramref name="man"/> until <paramref name="now"/> + <see cref="LeaseSeconds"/>.
        /// A full table replaces the lease that runs out first. False when the type is not registered.</summary>
        public static bool Pin(EntityRepository repo, Entity man, long memoryId, double now)
        {
            if (memoryId == 0 || !repo.IsComponentTypeRegistered<Alerted>() || !repo.IsAlive(man)) return false;
            if (!repo.HasComponent<Alerted>(man)) repo.AddComponent(man, new Alerted());
            ref var a = ref repo.GetComponentRW<Alerted>(man);
            double until = now + LeaseSeconds;
            int slot = -1, oldest = 0;
            double oldestUntil = double.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                if (Id(ref a, i) == memoryId) { slot = i; break; }
                double u = Until(ref a, i);
                if (u < oldestUntil) { oldestUntil = u; oldest = i; }
            }
            Set(ref a, slot >= 0 ? slot : oldest, memoryId, until);
            return true;
        }

        /// <summary><paramref name="memoryId"/> is pinned on <paramref name="man"/> at <paramref name="now"/>.</summary>
        public static bool IsPinned(ISimulationView view, Entity man, long memoryId, double now)
        {
            if (memoryId == 0 || !view.HasComponent<Alerted>(man)) return false;
            var a = view.GetComponentRO<Alerted>(man);
            for (int i = 0; i < 4; i++)
                if (Id(ref a, i) == memoryId && Until(ref a, i) > now) return true;
            return false;
        }

        private static long Id(ref Alerted a, int i) => i switch { 0 => a.Id0, 1 => a.Id1, 2 => a.Id2, _ => a.Id3 };
        private static double Until(ref Alerted a, int i) => i switch { 0 => a.Until0, 1 => a.Until1, 2 => a.Until2, _ => a.Until3 };

        private static void Set(ref Alerted a, int i, long id, double until)
        {
            switch (i)
            {
                case 0: a.Id0 = id; a.Until0 = until; break;
                case 1: a.Id1 = id; a.Until1 = until; break;
                case 2: a.Id2 = id; a.Until2 = until; break;
                default: a.Id3 = id; a.Until3 = until; break;
            }
        }
    }
}
