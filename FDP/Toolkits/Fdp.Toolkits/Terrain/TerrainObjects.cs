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
    /// Every reader builds a <see cref="DoorStates"/> table from the view it runs on (R-219) — sight, fire and (5c) the navmesh.
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
    /// ⭐⭐ R-219 (📄 docs/DESIGN_Building_Interiors.md §3j "5b′") — the door states AS ONE VIEW SEES THEM: built from the
    /// <see cref="DoorState"/> + <see cref="TerrainObjectKey"/> entities of that view, index-aligned with <see cref="TerrainWorld.Doors"/>,
    /// immutable. A background module builds it from ITS snapshot, so its sight / fire / path queries see the doors of the tick it
    /// runs on — never a live value written mid-batch on the main thread.
    /// <para>⛔ Why not a field on <see cref="TerrainWorld"/> (5a/5b as first built): the terrain is shared BY REFERENCE into every
    /// background snapshot, so live state on it reaches every thread at once. ⛔ Why not a singleton: a background snapshot SHARES
    /// singleton tables with the live world (<c>EntityRepository.SyncSingletonById</c>), so a singleton swap reaches every snapshot
    /// too. Per-entity components are the one thing a snapshot really copies.</para>
    /// </summary>
    public sealed class DoorStates
    {
        private readonly byte[] _states;

        private DoorStates(byte[] states) => _states = states;

        /// <summary>Door <paramref name="index"/>'s state in this view.</summary>
        public TerrainDoorState this[int index] => (TerrainDoorState)_states[index];

        public int Count => _states.Length;

        /// <summary>The doors of <paramref name="world"/> as the terrain authored them (no door entity says otherwise).</summary>
        public static DoorStates Authored(TerrainWorld world)
        {
            var a = new byte[world.Doors.Count];
            for (int i = 0; i < a.Length; i++) a[i] = (byte)world.Doors[i].Initial;
            return new DoorStates(a);
        }

        /// <summary>
        /// The doors of <paramref name="world"/> as <paramref name="view"/> sees them: each door entity's <see cref="DoorState"/>, else
        /// the authored state. Cheap — one pass over the door entities; build it once per batch / per system tick.
        /// <para>⭐ R-220 — allocates nothing while the doors are unchanged: it ALWAYS re-reads the door entities (into this thread's
        /// scratch) and hands back this thread's previous table when every state is the same. ⛔ Never keyed on a version — a door
        /// written within the tick would be served stale. A table is immutable once returned, so a caller may hold it for its batch.</para>
        /// </summary>
        public static DoorStates Of(ISimulationView view, TerrainWorld world)
        {
            int n = world.Doors.Count;
            var a = t_scratch is { } sc && sc.Length == n ? sc : (t_scratch = new byte[n]);
            for (int i = 0; i < n; i++) a[i] = (byte)world.Doors[i].Initial;
            if (n > 0)
            {
                var query = DoorQuery(view);
                if (query != null)
                    foreach (var e in query)
                    {
                        int i = world.DoorIndexOf(view.GetManagedComponentRO<TerrainObjectKey>(e).Key);
                        if (i >= 0) a[i] = (byte)view.GetComponentRO<DoorState>(e).State;
                    }
            }

            if (t_last is { } last && last._states.AsSpan().SequenceEqual(a)) return last;
            return t_last = new DoorStates(a.ToArray());   // the doors changed (or a new world/thread) — the one allocation
        }

        // ⭐ R-220 — per THREAD (a background module reads its own snapshot on its own thread): the scratch the doors are read into,
        //   the last table handed out, and the door query of the last repository (an EntityQuery is immutable and reusable).
        [ThreadStatic] private static byte[]? t_scratch;
        [ThreadStatic] private static DoorStates? t_last;
        [ThreadStatic] private static EntityRepository? t_queryRepo;
        [ThreadStatic] private static EntityQuery? t_query;

        private static EntityQuery? DoorQuery(ISimulationView view)
        {
            if (view is not EntityRepository repo)
                return view.Query().With<DoorState>().WithManaged<TerrainObjectKey>().WithLifecycle(EntityLifecycle.All).Build();
            if (ReferenceEquals(repo, t_queryRepo)) return t_query;
            if (!repo.IsComponentTypeRegistered<DoorState>()) return null;   // not cached: the type may be registered later
            t_query = repo.Query().With<DoorState>().WithManaged<TerrainObjectKey>().WithLifecycle(EntityLifecycle.All).Build();
            t_queryRepo = repo;
            return t_query;
        }

        /// <summary>The resident terrain's doors as <paramref name="view"/> sees them, or null when no terrain is resident / it has no doors.</summary>
        public static DoorStates? Of(ISimulationView view)
            => view is EntityRepository repo && repo.HasSingletonManaged<TerrainWorld>() && repo.GetSingletonManaged<TerrainWorld>() is { Doors.Count: > 0 } w
                ? Of(view, w) : null;
    }
}
