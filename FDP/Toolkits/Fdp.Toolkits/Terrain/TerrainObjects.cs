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

        private DoorStates(byte[] states, BuildingDamageTable? damage = null) { _states = states; Damage = damage; }

        /// <summary>
        /// ⭐ <c>CE-4391</c> S-1 (UTM §8.2 B-3) — the wall breaches of this view (the union of every <see cref="BuildingDamage"/> entity), or null when
        /// it holds none. Rides here because every sight / fire / sound / path caller already passes ONE <see cref="DoorStates"/> (a second parameter
        /// through ~30 files is the silent-default pattern). A table held by a caller keeps the breaches it was built with; the next
        /// <see cref="Of(ISimulationView, TerrainWorld)"/> hands a new instance over the same door array when the damage changed.
        /// </summary>
        public BuildingDamageTable? Damage { get; }

        /// <summary>Door <paramref name="index"/>'s state in this view.</summary>
        public TerrainDoorState this[int index]
            => Damage != null && Damage.DoorDestroyed(index) ? TerrainDoorState.Destroyed   // ⭐ CE-4391 S-2 — a collapsed building's doors are gone
               : (TerrainDoorState)_states[index];

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
            long t0 = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();   // ⭐ CE-3427
            var r = OfCore(view, world);
            // ⭐ CE-4391 S-1 (B-3) — the breaches of this view ride along. The door table (above) is cached on its own; the damage table has its
            //   own stamp, so a breach written this frame forces a rebuild of IT and of nothing else, and an unchanged frame hands back the same instance.
            var damage = world.Panels.Count > 0 ? BuildingDamageTable.Of(view, world) : null;   // ⭐ CE-4599 — free walls too
            if (!ReferenceEquals(r.Damage, damage)) r = t_last = new DoorStates(r._states, damage);   // a new instance over the SAME door array
            Fdp.Toolkit.Diagnostics.QueryTimings.End(S_Of, t0);
            return r;
        }
        private static readonly int S_Of = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("doors.DoorStates.Of");

        private static DoorStates OfCore(ISimulationView view, TerrainWorld world)
        {
            // ⭐ CE-2414 — the table this thread built THIS frame is handed back while nothing of the doors was written since the frame
            //   began and every door entity it read is still a door (FrameStamp: R-220's "a door written within the tick" forces a rebuild).
            //   📐 It walked every entity per sight / fire query: ~8 % of the SimHost thread on the CE-2408 stress world.
            // ⭐ CE-3427 — and while NOTHING structural happened since (no entity destroyed, no component added or removed —
            //   EntityRepository.StructuralChanges), the door entities it read are still doors without walking them: O(1). 📐 The walk
            //   was 239 µs per call on scale-1 (6 911 doors), ~59 % of the EQS sensors' time at 1 000 men; a background solve's snapshot
            //   has no structural change within its run, so every call after the first is the O(1) answer. After a structural change the
            //   walk decides, as before, and a passing walk refreshes the mark. ⚠ A snapshot's SyncFrom does NOT count structural changes —
            //   safe because it takes the live GlobalVersion, so a re-synced snapshot always fails the frame stamp above first (a module
            //   syncs at most once per live frame).
            if (view is EntityRepository live && t_last is { } held && ReferenceEquals(world, t_world)
                && t_stamp.Holds(live, typeof(DoorState)))
            {
                if (live.StructuralChanges == t_structural) return held;
                if (StillDoors(live)) { t_structural = live.StructuralChanges; return held; }
            }
            // ⭐ CE-3427 — a NEW frame: the table still holds when no door state was written since it was built, no door entity was added
            //   (the add counter), and every door entity it read is still one (the walk — it always runs here, so a destroy a snapshot's
            //   sync did not count is still seen). 📐 The rebuild (a query + a key lookup per door entity) was ≈ 2 ms a frame on the CGF
            //   main thread on scale-1; the walk is a fraction of it.
            else if (view is EntityRepository next && t_last is { } kept && ReferenceEquals(world, t_world)
                     && t_stamp.HoldsSinceTaken(next, typeof(DoorState))
                     && next.ComponentsAddedOf(ComponentType<DoorState>.ID) == t_doorsAdded && StillDoors(next))
            {
                t_stamp.Take(next);
                t_structural = next.StructuralChanges;
                return kept;
            }
            t_builds++;
            int n = world.Doors.Count;
            var a = t_scratch is { } sc && sc.Length == n ? sc : (t_scratch = new byte[n]);
            for (int i = 0; i < n; i++) a[i] = (byte)world.Doors[i].Initial;
            if (n > 0)
            {
                var query = DoorQuery(view);
                t_doorCount = 0;
                if (query != null)
                    foreach (var e in query)
                    {
                        int i = world.DoorIndexOf(view.GetManagedComponentRO<TerrainObjectKey>(e).Key);
                        if (i >= 0) a[i] = (byte)view.GetComponentRO<DoorState>(e).State;
                        var doors = t_doors ??= new Entity[16];
                        if (t_doorCount == doors.Length) Array.Resize(ref t_doors, doors.Length * 2);
                        t_doors![t_doorCount++] = e;
                    }
            }
            if (view is EntityRepository built)
            {
                t_stamp.Take(built); t_world = world; t_structural = built.StructuralChanges;
                t_doorsAdded = built.IsComponentTypeRegistered<DoorState>() ? built.ComponentsAddedOf(ComponentType<DoorState>.ID) : -1;
            }
            else t_stamp.Clear();

            if (t_last is { } last && last._states.AsSpan().SequenceEqual(a)) return last;
            return t_last = new DoorStates(a.ToArray());   // the doors changed (or a new world/thread) — the one allocation
        }

        private static bool StillDoors(EntityRepository repo)
        {
            t_walks++;
            for (int k = 0; k < t_doorCount; k++)
                if (!repo.IsAlive(t_doors![k]) || !repo.HasComponent<DoorState>(t_doors[k])) return false;
            return true;
        }

        [ThreadStatic] private static Fdp.Toolkit.Replication.Utilities.FrameStamp t_stamp;
        [ThreadStatic] private static TerrainWorld? t_world;
        [ThreadStatic] private static Entity[]? t_doors;
        [ThreadStatic] private static int t_doorCount;
        [ThreadStatic] private static long t_structural;   // ⭐ CE-3427 — EntityRepository.StructuralChanges when the door entities were last known
        [ThreadStatic] private static int t_walks;
        [ThreadStatic] private static long t_doorsAdded;
        [ThreadStatic] private static int t_builds;

        /// <summary>⭐ CE-3427 — how many times THIS thread rebuilt the door table from the door entities (rails).</summary>
        internal static int DoorTableBuildsOnThisThread => t_builds;

        /// <summary>⭐ CE-3427 — how many times THIS thread walked the door entities to re-check them (rails).</summary>
        internal static int DoorWalksOnThisThread => t_walks;

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

        /// <summary>The resident terrain's doors (and, ⭐ CE-4391, breaches) as <paramref name="view"/> sees them, or null when no terrain is resident / it has neither doors nor buildings.</summary>
        public static DoorStates? Of(ISimulationView view)
            => view is EntityRepository repo && repo.HasSingletonManaged<TerrainWorld>() && repo.GetSingletonManaged<TerrainWorld>() is { HasLiveState: true } w
                ? Of(view, w) : null;
    }
}
