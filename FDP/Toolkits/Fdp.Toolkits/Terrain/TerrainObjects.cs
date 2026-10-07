using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ Buildings Stage 5b (📄 docs/DESIGN_Building_Interiors.md §3a, §3b, §3j) — a door entity's live state. The ONE writer is
    /// the door's owner (its creator today; door commands in 5d); every other node receives it through the <c>EntityDoorState</c>
    /// descriptor (R-136: entity state comes from the TKB or a published TransientLocal descriptor, never a per-node default).
    /// <see cref="DoorStateMirrorSystem"/> copies it into <see cref="TerrainWorld.SetDoorState"/> on every node, which is what
    /// sight, fire and (5c) the navmesh read.
    /// <para>⛔ <c>NoScenario</c>: a door entity is never saved (§3b K5 — the terrain recreates it); its state is saved by the
    /// scenario's <c>terrainObjects</c> section (5e), keyed by <see cref="TerrainObjectKey"/>.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.DoorState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct DoorState
    {
        public TerrainDoorState State;
    }

    /// <summary>
    /// ⭐ §3b K2 — the terrain-provided key of the object this entity stands for: <c>"&lt;terrain&gt;/&lt;building&gt;/&lt;doorId&gt;"</c>.
    /// A STRING, so it can never be mistaken for a scenario entity's saved (numeric) id. Immutable; set at birth.
    /// <para>⭐ The entities that carry it ARE the §3b K3 key → runtime-id map: <see cref="TerrainObjects.Find"/> walks them.
    /// ⛔ No second table is kept beside them — it would be a second representation of one fact.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.TerrainObjectKey)]
    [DataPolicy(DataPolicy.NoScenario)]
    public sealed record TerrainObjectKey
    {
        public string Key { get; init; } = "";
    }

    /// <summary>Reads over the terrain-object entities (Stage 5b).</summary>
    public static class TerrainObjects
    {
        /// <summary>The live entity standing for terrain object <paramref name="key"/>, or <see cref="Entity.Null"/>.</summary>
        public static Entity Find(EntityRepository repo, string key)
        {
            foreach (var e in repo.Query().WithManaged<TerrainObjectKey>().WithLifecycle(EntityLifecycle.All).Build())
                if (repo.GetComponent<TerrainObjectKey>(e).Key == key) return e;
            return Entity.Null;
        }

        /// <summary>Every terrain-object key that already has an entity in <paramref name="repo"/>.</summary>
        public static HashSet<string> ExistingKeys(EntityRepository repo)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in repo.Query().WithManaged<TerrainObjectKey>().WithLifecycle(EntityLifecycle.All).Build())
                keys.Add(repo.GetComponent<TerrainObjectKey>(e).Key);
            return keys;
        }
    }

    /// <summary>
    /// ⭐ Buildings Stage 5b — copies each door entity's replicated <see cref="DoorState"/> into the resident
    /// <see cref="TerrainWorld"/> (<see cref="TerrainWorld.SetDoorState"/>), on EVERY node: the queries that read door state
    /// (sight, fire, the 5c path filter) run where the terrain is, and the terrain is a plain model that knows no entities.
    /// <para>⭐ Built by the shared <c>EntityCreationPack</c> and scheduled by every host (<c>EntityCreation.TerrainObjectSystems</c>),
    /// whose <c>Unserviceable()</c> reports a host that forgets it — ⛔ a host without it would keep every door at its terrain
    /// default while the owner had opened it, silently.</para>
    /// <para>Runs in <see cref="SystemPhase.BeforeSync"/>: after the Input-phase ingress has been played back, before the
    /// Simulation-phase queries of the same frame.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public sealed class DoorStateMirrorSystem : IEcsModuleSystem
    {
        private TerrainWorld? _indexedFor;
        private readonly Dictionary<string, int> _doorIndex = new(StringComparer.Ordinal);

        /// <summary>How many door states this system wrote into a terrain (a rail reads it).</summary>
        public long Writes { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingletonManaged<TerrainWorld>()) return;
            var world = repo.GetSingletonManaged<TerrainWorld>();
            if (world == null || world.Doors.Count == 0) return;
            if (!repo.IsComponentTypeRegistered<DoorState>()) return;   // a managed query over an unregistered key matches nothing

            if (!ReferenceEquals(world, _indexedFor))
            {
                // a terrain commit swaps the singleton object — re-index its doors (and its door states start from the terrain)
                _indexedFor = world;
                _doorIndex.Clear();
                for (int i = 0; i < world.Doors.Count; i++) _doorIndex[world.Doors[i].Key] = i;
            }

            foreach (var e in repo.Query().With<DoorState>().WithManaged<TerrainObjectKey>().WithLifecycle(EntityLifecycle.All).Build())
            {
                if (!_doorIndex.TryGetValue(repo.GetComponent<TerrainObjectKey>(e).Key, out int i)) continue;
                var state = repo.GetComponentRO<DoorState>(e).State;
                if (world.DoorState(i) == state) continue;
                world.SetDoorState(i, state);
                Writes++;
            }
        }
    }
}
