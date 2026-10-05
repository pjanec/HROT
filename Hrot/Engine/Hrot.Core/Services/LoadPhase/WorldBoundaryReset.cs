using System;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐⭐ <c>CE-2101</c> — <b>the world boundary clears the world</b>, on every ECS host, before a load builds the new one.
/// 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §11b (the world is cleared at every load entered from Idle — that
/// is what makes the id authority's reset to 1000 safe) · <c>docs/DESIGN_Cluster_Load_Phase.md</c> (as-built, CE-2101).
///
/// <para>🔴 <b>Measured before this existed:</b> only the editor's EDIT path cleared (<c>ScenarioFileService.NewScenario</c>);
/// a LIVE load, and every load on SimHost / CGF / IG, kept the previous run's entities. The ids restarted at 1000, so
/// the respawn was dropped as already mapped (<c>NetworkSpawningSystem</c>) and the run continued on stale entities —
/// the rifleman started where the last run left it.</para>
///
/// <para>⭐ <b>DESTROYS every entity rather than <c>SoftClear</c>.</b> <c>SoftClear</c> wipes the entity index, so the
/// generations restart at 1 (<c>EntityIndex.Clear</c>) and a handle cached before the wipe ALIASES the new entity at the
/// same index. Destroying bumps each generation (<c>EntityIndex.cs:164</c>), so every stale handle goes dead instead.
/// Singletons survive (the terrain and the TKB are content the load steps keep or replace).</para>
/// </summary>
public static class WorldBoundaryReset
{
    /// <summary>
    /// Clears <paramref name="world"/> for a new load: every entity destroyed, the network entity map cleared
    /// (<paramref name="entityMap"/>, else the world's <see cref="NetworkEntityMap"/> singleton), the sim clock reset,
    /// and <c>WorldResetEvent</c> published where the event is registered. Returns the number of entities destroyed.
    /// </summary>
    public static int Clear(EntityRepository world, NetworkEntityMap? entityMap = null, string hostLabel = "node")
    {
        if (world == null) throw new ArgumentNullException(nameof(world));

        // The map holds entity handles — flush it before the entities go (the editor's reset-observer contract).
        var map = entityMap ?? (world.HasSingletonManaged<NetworkEntityMap>() ? world.GetSingletonManaged<NetworkEntityMap>() : null);
        map?.Clear();

        int destroyed = 0;
        for (int i = 0; i <= world.MaxEntityIndex; i++)
        {
            var e = world.GetEntityByIndex(i);
            if (!world.IsAlive(e)) continue;
            world.DestroyEntity(e);
            destroyed++;
        }

        // ⭐ Every translator's per-id bookkeeping belongs to the world that just ended (WorldEpoch).
        WorldEpoch.Advance(world);

        if (world.HasSingletonUnmanaged<GlobalTime>())
            world.SetSingletonUnmanaged(default(GlobalTime));

        if (world.Bus.IsRegistered<Hrot.Common.Events.WorldResetEvent>())
            world.Bus.Publish(new Hrot.Common.Events.WorldResetEvent());

        FdpLog<LoadPhaseChain>.Info(
            "[LoadPhase] {0}: world boundary — {1} entities cleared before the load (CE-2101).", hostLabel, destroyed);
        return destroyed;
    }
}
