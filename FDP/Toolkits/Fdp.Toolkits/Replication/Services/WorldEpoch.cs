using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Replication.Services
{
    /// <summary>
    /// ⭐⭐ <c>CE-2101</c> — <b>which world this is.</b> A counter bumped every time the world is cleared for a new load (the
    /// world boundary). A translator that keeps LOCAL per-network-id bookkeeping — "already published once", "last value
    /// sent", "deferred until the entity exists" — clears it when the epoch moves, because the ids restart at 1000 at the
    /// boundary and the next run's entities REUSE them.
    ///
    /// <para>🔴 <b>Measured:</b> <c>EntityMasterEgressTranslator</c>'s "exactly once per network id" set outlived the wipe, so
    /// the second run's entity 1000 never published its master descriptor; its ghost on SimHost never learned its TKB type,
    /// was never promoted and never got its sensors — the unit perceived nothing.</para>
    ///
    /// <para>⛔ <b>NOT a dispose.</b> <c>IDescriptorTranslator.Dispose(id)</c> means "the entity was deleted" and writes a DDS
    /// dispose; at a world boundary every node wipes its own world, and a late dispose of a REUSED id could destroy the new
    /// run's ghost. This is local bookkeeping only — nothing goes on the wire.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.WorldEpoch)]
    [DataPolicy(DataPolicy.Transient)]
    public sealed class WorldEpoch
    {
        /// <summary>The current world's number (starts at 0; the first world boundary makes it 1).</summary>
        public int Value { get; private set; }

        /// <summary>⭐ <c>D4</c> — how many times THIS world was restored in place (a dry-run exit, a checkpoint restore).</summary>
        public int RestoreCount { get; private set; }

        /// <summary>⭐ <c>D4</c> — bumped by BOTH a world boundary and a restore: what <see cref="Moved"/> watches.</summary>
        public int Generation { get; private set; }

        /// <summary>Starts the next world. Called by the world boundary reset on every host.</summary>
        public static void Advance(EntityRepository world)
        {
            var epoch = Get(world);
            epoch.Value++;
            epoch.Generation++;
        }

        /// <summary>
        /// ⭐⭐ <c>D4</c> (restore design) — <b>this world was put back in place</b> (<c>AfterRestore</c>): every per-id cache
        /// keyed on <see cref="Moved"/> clears, because the entities it describes were just replaced. ⛔ <see cref="Ended"/>
        /// does NOT move: the world did not end, so nothing deletes its entities on the wire
        /// (<c>CycloneNetworkCleanupSystem</c>). 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md D4.
        /// </summary>
        public static void Restored(EntityRepository world)
        {
            var epoch = Get(world);
            epoch.RestoreCount++;
            epoch.Generation++;
        }

        private static WorldEpoch Get(EntityRepository world)
        {
            var epoch = world.HasSingletonManaged<WorldEpoch>() ? world.GetSingletonManaged<WorldEpoch>() : null;
            if (epoch == null) { epoch = new WorldEpoch(); world.SetSingletonManaged(epoch); }
            return epoch;
        }

        /// <summary>The current epoch of <paramref name="view"/>'s world (0 when no boundary has happened).</summary>
        public static int Of(ISimulationView view)
            => view is EntityRepository repo && repo.HasSingletonManaged<WorldEpoch>() ? repo.GetSingletonManaged<WorldEpoch>()?.Value ?? 0 : 0;

        private static int GenerationOf(ISimulationView view)
            => view is EntityRepository repo && repo.HasSingletonManaged<WorldEpoch>() ? repo.GetSingletonManaged<WorldEpoch>()?.Generation ?? 0 : 0;

        /// <summary>
        /// True once per world REPLACEMENT since <paramref name="seen"/> — a world boundary OR an in-place restore (D4); the
        /// caller then clears its per-id state. <c>if (WorldEpoch.Moved(view, ref _epoch)) _publishedNetIds.Clear();</c>
        /// </summary>
        public static bool Moved(ISimulationView view, ref int seen)
        {
            int now = GenerationOf(view);
            if (now == seen) return false;
            seen = now;
            return true;
        }

        /// <summary>
        /// ⭐ <c>D4</c> — true once per world BOUNDARY only (the world ENDED; a restore does not count). For the one consumer
        /// whose reaction acts on the wire: deleting this node's owned entities (<c>CycloneNetworkCleanupSystem</c>).
        /// </summary>
        public static bool Ended(ISimulationView view, ref int seen)
        {
            int now = Of(view);
            if (now == seen) return false;
            seen = now;
            return true;
        }
    }
}
