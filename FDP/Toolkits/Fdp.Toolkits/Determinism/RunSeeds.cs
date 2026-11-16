using Fdp.Core;
using Fdp.Toolkit.Aggregate;

namespace Fdp.Toolkit.Determinism
{
    /// <summary>
    /// ⭐⭐ <c>CE-8003</c> (C-8, RV-3) — keeps <see cref="SimRng.RunSeed"/> and the world's recorded <see cref="RunSeed"/> singleton one value:
    /// a world that carries a seed (a loaded recording, a scenario that set one) gives it to <see cref="SimRng"/>; a world without one is
    /// stamped with the process's seed, so the recording carries the seed it ran with. Serial only (never inside a parallel phase).
    /// </summary>
    public static class RunSeeds
    {
        /// <summary>Syncs and returns the run seed in force for <paramref name="world"/>. 0 B.</summary>
        public static uint Sync(EntityRepository world)
        {
            if (world.HasSingleton<RunSeed>())
            {
                uint v = world.GetSingleton<RunSeed>().Value;
                if (v != SimRng.RunSeed) SimRng.RunSeed = v;
                return v;
            }
            if (SimRng.RunSeed != 0) world.SetSingleton(new RunSeed { Value = SimRng.RunSeed });
            return SimRng.RunSeed;
        }
    }
}
