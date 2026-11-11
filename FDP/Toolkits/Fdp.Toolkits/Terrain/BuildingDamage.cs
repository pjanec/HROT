using System;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>What a wall breach is. 📄 docs/DESIGN_Terrain_Import_UTM.md §8 D-2.</summary>
    public enum BreachKind : byte
    {
        /// <summary>Sight and fire pass; a man does not (a shell hole, a window blown out).</summary>
        Hole = 0,
        /// <summary>A hole whose bottom is on the floor and which is at least <c>0.8 × 1.8 m</c>: walkable (S-3 makes the navmesh follow).</summary>
        Passage = 1,
    }

    /// <summary>
    /// ⭐ <c>CE-4391</c> S-1 (UTM §8.2 B-4) — one breach in a wall panel: a rectangle ON the panel, in the panel's own coordinates — metres
    /// along it from its <c>A</c> end (<see cref="From"/>..<see cref="To"/>) and absolute Z (<see cref="Bottom"/>..<see cref="Top"/>) — clipped to
    /// the panel's length and storey. <see cref="Panel"/> is the GLOBAL index into <see cref="TerrainWorld.Panels"/>.
    /// </summary>
    public readonly record struct WallBreach(int Panel, float From, float To, float Bottom, float Top, BreachKind Kind)
    {
        public float Width => To - From;
        public float Height => Top - Bottom;

        /// <summary>True when the point (<paramref name="along"/> m from the panel's A end, absolute <paramref name="z"/>) is inside the rectangle.</summary>
        public bool Contains(float along, float z) => along >= From && along <= To && z >= Bottom && z <= Top;

        /// <summary>True when the two breaches are on the same panel and their rectangles overlap or touch.</summary>
        public bool Overlaps(in WallBreach o)
            => Panel == o.Panel && From <= o.To && To >= o.From && Bottom <= o.Top && Top >= o.Bottom;

        /// <summary>The smallest rectangle holding both (same panel assumed); <paramref name="kind"/> is the caller's verdict on it.</summary>
        public WallBreach UnionWith(in WallBreach o, BreachKind kind)
            => new(Panel, MathF.Min(From, o.From), MathF.Max(To, o.To), MathF.Min(Bottom, o.Bottom), MathF.Max(Top, o.Top), kind);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4391</c> S-1 (docs/DESIGN_Terrain_Import_UTM.md §8 D-3 / §8.2 B-1, B-2, B-4) — the damage of ONE building: its breaches (and,
    /// S-2, whether it has collapsed). A runtime entity (with a <see cref="TerrainObjectKey"/> <c>"&lt;terrain&gt;/&lt;building label&gt;"</c> and a
    /// transform) made by a deciding node at ITS first breach of the building and owned by it; every node reads the UNION of every such entity
    /// through <see cref="BuildingDamageTable"/>, carried on <see cref="DoorStates.Damage"/> (so sight, fire, sound and paths see a breach in the
    /// frame after it is written). Replicated as <c>dtBuildingDamage</c>; unmanaged ⇒ recorded by bytes and restored by a checkpoint.
    /// <para>Capacity <see cref="Capacity"/> (B-4: four facades × four storeys hit once); a breach overlapping one on the same panel is MERGED
    /// (the union rectangle), and past capacity the owner opens a second entity for the same building (the view unions them).</para>
    /// <para>⛔ <c>NoScenario</c>: the entity is never saved as an entity (the terrain's object section saves its breaches, S-4).</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.BuildingDamage)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct BuildingDamage
    {
        /// <summary>How many breaches one entity holds.</summary>
        public const int Capacity = 16;

        /// <summary>The building's index in <see cref="TerrainWorld.Buildings"/> (the same terrain on every node).</summary>
        public int Building;
        /// <summary>1 once the building has collapsed (S-2; present from S-1 so the layout and the wire do not change, unused until then).</summary>
        public byte Collapsed;
        /// <summary>Breaches in use.</summary>
        public byte Count;

        public fixed int Panel[Capacity];
        public fixed float From[Capacity];
        public fixed float To[Capacity];
        public fixed float Bottom[Capacity];
        public fixed float Top[Capacity];
        public fixed byte Kind[Capacity];

        /// <summary>Breach <paramref name="i"/> (0 ≤ i &lt; <see cref="Count"/>).</summary>
        public readonly WallBreach Get(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return new WallBreach(Panel[i], From[i], To[i], Bottom[i], Top[i], (BreachKind)Kind[i]);
        }

        /// <summary>Overwrites slot <paramref name="i"/> (does not change <see cref="Count"/>).</summary>
        public void Set(int i, in WallBreach b)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            Panel[i] = b.Panel; From[i] = b.From; To[i] = b.To; Bottom[i] = b.Bottom; Top[i] = b.Top; Kind[i] = (byte)b.Kind;
        }

        /// <summary>This entity's breaches as a fresh array (a test / diagnostics read; ⛔ never per frame).</summary>
        public readonly WallBreach[] ToArray()
        {
            var a = new WallBreach[Count];
            for (int i = 0; i < a.Length; i++) a[i] = Get(i);
            return a;
        }
    }

    /// <summary>The outcome of <see cref="BuildingDamages.Add"/>.</summary>
    public enum BreachAdd : byte
    {
        /// <summary>The list already covered it (a second shell into the same hole): nothing written.</summary>
        Unchanged = 0,
        /// <summary>Added as a new breach.</summary>
        Added = 1,
        /// <summary>Merged with one or more overlapping breaches (the union rectangle).</summary>
        Merged = 2,
        /// <summary>The entity holds <see cref="BuildingDamage.Capacity"/> and the breach does not merge: the caller opens another entity.</summary>
        Full = 3,
    }

    /// <summary>⭐ <c>CE-4391</c> S-1 — the one writer-side rule for a <see cref="BuildingDamage"/>'s list (B-4): merge overlapping, else append.</summary>
    public static class BuildingDamages
    {
        /// <summary>⭐ The TKB type of a building-damage entity (bare: the state travels on <see cref="BuildingDamage"/> / <c>dtBuildingDamage</c>).</summary>
        public const long TkbType = 7203;

        /// <summary>The terrain-object key of building <paramref name="building"/>: <c>"&lt;terrain&gt;/&lt;label&gt;"</c> (a world with no name drops the first part).</summary>
        public static string KeyOf(TerrainWorld world, int building)
            => string.IsNullOrEmpty(world.Name) ? world.Buildings[building].Label : world.Name + "/" + world.Buildings[building].Label;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TerrainWorld, System.Collections.Generic.Dictionary<string, int>> s_byKey = new();

        /// <summary>
        /// ⭐ CE-4427 R-4 — re-keys <paramref name="d"/> from the world <paramref name="from"/> it was written against to the world <paramref name="to"/> that replaces it: the building by its stable KEY
        /// (<see cref="KeyOf"/> → <see cref="IndexOfKey"/>; or <paramref name="key"/>, the entity's own key, when given), each breach's panel by its within-building rank (<see cref="BuildingPanelIndex"/>). The live overlay re-sorts its parts when a block changes, so a house's index
        /// and its panels' indices MOVE; a damage entity that kept the old numbers would breach another house. A building the new world no longer has loses its damage (Building −1, no breaches). Returns true when
        /// <paramref name="d"/> changed; allocation-free.
        /// </summary>
        public static bool Remap(ref BuildingDamage d, TerrainWorld from, TerrainWorld to, string? key = null)
        {
            if (ReferenceEquals(from, to)) return false;
            int nb = -1;
            // ⭐ CE-3439 — the entity's OWN key (its TerrainObjectKey) wins when it has one: an Unpack re-keys a house, and the old numbering's label no longer names it in the new world
            if (!string.IsNullOrEmpty(key)) nb = IndexOfKey(to, key);
            else if ((uint)d.Building < (uint)from.Buildings.Count) nb = IndexOfKey(to, KeyOf(from, d.Building));
            var n = default(BuildingDamage);
            n.Building = nb;
            if (nb >= 0)
            {
                n.Collapsed = d.Collapsed;
                int k = 0;
                for (int i = 0; i < d.Count; i++)
                {
                    var b = d.Get(i);
                    int local = BuildingPanelIndex.ToLocal(from, b.Panel);
                    int panel = local >= 0 ? BuildingPanelIndex.ToGlobal(to, nb, local) : -1;
                    if (panel >= 0) n.Set(k++, b with { Panel = panel });
                }
                n.Count = (byte)k;
            }
            bool changed = n.Building != d.Building || n.Collapsed != d.Collapsed || n.Count != d.Count;
            for (int i = 0; !changed && i < n.Count; i++) changed = n.Get(i) != d.Get(i);
            if (changed) d = n;
            return changed;
        }

        /// <summary>
        /// ⭐ <c>CE-4417</c> (Terrain Authoring §18 C-4, O-3) — the index of the building <paramref name="key"/> names in <paramref name="world"/>, or −1.
        /// Accepts the full key (<see cref="KeyOf"/>, <c>"&lt;terrain&gt;/&lt;label&gt;"</c>) and the bare label, so a key written before or after a
        /// terrain rename resolves the same house. Built once per world (a world is immutable), so a receiving node maps a replicated key to ITS
        /// index — never trusting the sender's index, which differs when the two worlds were assembled differently.
        /// </summary>
        public static int IndexOfKey(TerrainWorld world, string? key)
        {
            if (world == null || string.IsNullOrEmpty(key)) return -1;
            var map = s_byKey.GetValue(world, static w =>
            {
                var m = new System.Collections.Generic.Dictionary<string, int>(w.Buildings.Count * 2, StringComparer.Ordinal);
                for (int i = 0; i < w.Buildings.Count; i++)
                {
                    m.TryAdd(KeyOf(w, i), i);
                    if (!string.IsNullOrEmpty(w.Buildings[i].Label)) m.TryAdd(w.Buildings[i].Label, i);
                }
                return m;
            });
            return map.TryGetValue(key, out int b) ? b : -1;
        }

        /// <summary>
        /// Adds <paramref name="b"/> to <paramref name="d"/>: every breach it overlaps on its panel is absorbed into the union rectangle (repeatedly, until
        /// nothing overlaps); the union's kind is re-decided from <paramref name="world"/> when it is given. ⭐ Atomic: on <see cref="BreachAdd.Full"/>
        /// or <see cref="BreachAdd.Unchanged"/> <paramref name="d"/> is left exactly as it was. Allocation-free.
        /// </summary>
        public static BreachAdd Add(ref BuildingDamage d, in WallBreach b, TerrainWorld? world)
        {
            int n = d.Count;
            Span<WallBreach> list = stackalloc WallBreach[BuildingDamage.Capacity + 1];
            for (int i = 0; i < n; i++) list[i] = d.Get(i);

            var merged = b;
            bool absorbed = false;
            for (bool again = true; again;)
            {
                again = false;
                for (int i = 0; i < n; i++)
                {
                    if (!list[i].Overlaps(merged)) continue;
                    var kind = (BreachKind)Math.Max((byte)list[i].Kind, (byte)merged.Kind);
                    merged = list[i].UnionWith(merged, kind);
                    for (int k = i + 1; k < n; k++) list[k - 1] = list[k];   // keep the order of the rest
                    n--;
                    absorbed = again = true;
                    break;
                }
            }
            if (absorbed && world != null && merged.Panel >= 0 && merged.Panel < world.Panels.Count)
                merged = merged with { Kind = BreachRule.Classify(world.Panels[merged.Panel], world.Breaches, merged.From, merged.To, merged.Bottom, merged.Top) };

            // the union equals an existing breach: nothing new (a second shell into the same hole)
            if (absorbed)
                for (int i = 0; i < d.Count; i++)
                    if (d.Get(i) == merged && n == d.Count - 1) return BreachAdd.Unchanged;

            if (n >= BuildingDamage.Capacity) return BreachAdd.Full;
            list[n++] = merged;
            for (int i = 0; i < n; i++) d.Set(i, list[i]);
            d.Count = (byte)n;
            return absorbed ? BreachAdd.Merged : BreachAdd.Added;
        }
    }
}
