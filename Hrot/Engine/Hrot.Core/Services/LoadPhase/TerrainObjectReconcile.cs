using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Terrain;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐⭐ <c>CE-5028</c> BE-2 (📄 docs/DESIGN_Terrain_Base_Edits.md D-4; closes Terrain Authoring G-3) — the terrain's ENTITIES follow its world on every commit of a new
/// one (<c>TerrainResidency.CommitObstacles</c>, main thread): a door entity whose key the world no longer has is DESTROYED (a removed building's doors go with it, 🔒
/// user), a building-damage entity whose building is gone (re-keyed to −1 by the remap that runs first) is destroyed with its windows, and a door the world GAINED (an
/// overlay house, a made-enterable building) gets its entity — <see cref="TerrainObjectRequests.ForDoors"/>, the load step's own request.
/// <para>⭐ Who: a node destroys only what it OWNS. It creates only when it is the node that made the doors — it owns door entities already — or, in a world that
/// has none yet, always (the editor, a single node). ⚠ So a cluster whose terrain started with NO doors gets its first new doors from every baking node; the live
/// overlay is the editor's (one node), so that case is not reached today — say so before building one that reaches it.</para>
/// </summary>
public static class TerrainObjectReconcile
{
    /// <summary>What one reconcile did (rails, diagnostics).</summary>
    public readonly record struct Result(int DoorsDestroyed, int DamageDestroyed, int DoorsRequested);

    /// <param name="replacing">True when this commit REPLACES an earlier world (a live bake). ⛔ No door is created on a world's first commit: the scenario load
    /// step creates the doors then (<c>ScenarioLoadStep</c>), and two creators would make each door twice.</param>
    public static Result Reconcile(EntityRepository world, TerrainWorld terrain, bool replacing = true)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (terrain == null) throw new ArgumentNullException(nameof(terrain));
        int doorsGone = 0, damageGone = 0, asked = 0;
        var doomed = new List<Entity>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        bool ownsADoor = false, anyDoor = false;

        if (world.IsComponentTypeRegistered<DoorState>())
            foreach (var e in world.Query().With<DoorState>().WithManaged<TerrainObjectKey>().Build())
            {
                anyDoor = true;
                string key = world.GetComponent<TerrainObjectKey>(e).Key;
                bool owned = world.HasAuthority(e);
                ownsADoor |= owned;
                if (!string.IsNullOrEmpty(key) && terrain.DoorIndexOf(key) >= 0) { keys.Add(key); continue; }
                if (owned) doomed.Add(e); else if (!string.IsNullOrEmpty(key)) keys.Add(key);   // another node's: its owner removes it
            }
        foreach (var e in doomed) { world.DestroyEntity(e); doorsGone++; }

        doomed.Clear();
        if (world.IsComponentTypeRegistered<BuildingDamage>())
            foreach (var e in world.Query().With<BuildingDamage>().Build())
                if (world.GetComponentRO<BuildingDamage>(e).Building < 0 && BuildingDamageSystem.OwnsDamage(world, e)) doomed.Add(e);
        foreach (var e in doomed) { world.DestroyEntity(e); damageGone++; }

        // a door asked for by an earlier commit whose entity is not made yet is not asked for again (two bakes can commit before the creation system runs)
        var asking = s_asked.GetValue(world, static _ => new HashSet<string>(StringComparer.Ordinal));
        asking.RemoveWhere(keys.Contains);   // made since: forget it
        if (replacing && terrain.Doors.Count > 0 && (ownsADoor || !anyDoor))
        {
            keys.UnionWith(asking);
            foreach (var request in TerrainObjectRequests.ForDoors(terrain, keys, idAllocator: null))
            {
                world.Bus.PublishManaged(request);
                foreach (var c in request.InitialComponents!) if (c is TerrainObjectKey k) asking.Add(k.Key);
                asked++;
            }
        }
        return new Result(doorsGone, damageGone, asked);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, HashSet<string>> s_asked = new();
}
