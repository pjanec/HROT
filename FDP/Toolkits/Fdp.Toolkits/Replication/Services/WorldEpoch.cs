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

        /// <summary>Starts the next world. Called by the world boundary reset on every host.</summary>
        public static void Advance(EntityRepository world)
        {
            var epoch = world.HasSingletonManaged<WorldEpoch>() ? world.GetSingletonManaged<WorldEpoch>() : null;
            if (epoch == null) { epoch = new WorldEpoch(); world.SetSingletonManaged(epoch); }
            epoch.Value++;
        }

        /// <summary>The current epoch of <paramref name="view"/>'s world (0 when no boundary has happened).</summary>
        public static int Of(ISimulationView view)
            => view is EntityRepository repo && repo.HasSingletonManaged<WorldEpoch>() ? repo.GetSingletonManaged<WorldEpoch>()?.Value ?? 0 : 0;

        /// <summary>
        /// True once per epoch change since <paramref name="seen"/> — the caller then clears its per-id state.
        /// <c>if (WorldEpoch.Moved(view, ref _epoch)) _publishedNetIds.Clear();</c>
        /// </summary>
        public static bool Moved(ISimulationView view, ref int seen)
        {
            int now = Of(view);
            if (now == seen) return false;
            seen = now;
            return true;
        }
    }
}
