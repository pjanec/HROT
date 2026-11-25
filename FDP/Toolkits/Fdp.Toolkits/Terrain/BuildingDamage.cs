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
    /// ⭐ <c>CE-11054</c> — WHAT made a breach (or collapsed a building). The shape is <see cref="BreachKind"/>; this is the cause, so a client scorches a rim a blast made and not one a bulldozer
    /// cut. ⭐ <see cref="Unknown"/> MUST stay 0: an unset byte (an old record, a replay from before this, a load of a save without it) reads as Unknown.
    /// </summary>
    public enum BreachCause : byte
    {
        Unknown = 0,
        /// <summary>A warhead's blast (<c>AreaEffectSystem</c>): scorched, throws debris away from the blast point.</summary>
        Detonation = 1,
        /// <summary>A hull rammed the wall (a building's bump, a free wall rammed at speed).</summary>
        Ram = 2,
        /// <summary>A blade cut it (<c>ContactBreach</c> blade row, <c>BladeCut</c>, the AI dozer's cut).</summary>
        Blade = 3,
        /// <summary>A free wall pushed through by a held full-throttle push (a building is only ever rammed or bladed, D-3).</summary>
        Push = 4,
    }

    /// <summary>
    /// ⭐ <c>CE-11051</c> / <c>CE-11054</c> — everything a breach remembers about how it came to be: the sim time (s; 0 = from a save), the blast point (terrain-local metres; a hull's centre for a
    /// contact cause), the <see cref="BreachCause"/>, the warhead's explosive kg the breach rule used (0 for contact) and the acting vehicle's NETWORK id (contact causes; 0 otherwise and after a load).
    /// </summary>
    public readonly record struct BreachOrigin(SimInstant At, System.Numerics.Vector3 Blast, BreachCause Cause = BreachCause.Unknown, float ExplosiveKg = 0f, int Actor = 0);

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

        /// <summary>
        /// ⭐ <c>CE-11051</c> — WHEN the building collapsed, seconds of sim time (the clock the recorder and the gateway use, <c>SimClock.TotalTime</c>); 0 = not
        /// collapsed, or collapsed in a SAVE this run loaded (no live change). Written by <see cref="BuildingDamageSystem"/> in the frame the record changed.
        /// </summary>
        public double CollapsedAt;
        /// <summary>⭐ <c>CE-11051</c> — per breach: the sim time (s) it was made or last WIDENED (a merge that grew it); 0 = loaded from a save. A second
        /// shell into the same hole changes nothing, so moves nothing. A fixed buffer holds no <see cref="SimInstant"/>; <see cref="MadeAtOf"/> types it.</summary>
        public fixed double MadeAt[Capacity];
        /// <summary>⭐ <c>CE-11051</c> — per breach: the BLAST POINT that made (or last widened) it — the detonation's world point, terrain-local metres (the frame
        /// of <c>SimTransform</c>); for a hull's ram or a blade, the hull's centre. Meaningless when <see cref="MadeAt"/> is 0. Debris flies away from it.</summary>
        public fixed float BlastX[Capacity];
        public fixed float BlastY[Capacity];
        public fixed float BlastZ[Capacity];

        /// <summary>⭐ <c>CE-11054</c> — per breach: its <see cref="BreachCause"/> (0 = Unknown). A merge: Detonation wins, else the newer cause.</summary>
        public fixed byte Cause[Capacity];
        /// <summary>⭐ <c>CE-11054</c> — per breach: the explosive kg the breach rule used (a merge keeps the largest; 0 for a contact cause).</summary>
        public fixed float ExplosiveKg[Capacity];
        /// <summary>⭐ <c>CE-11054</c> — per breach: the NETWORK id of the vehicle that rammed / cut / pushed it (the newest contact's; 0 = none, a detonation, or loaded from a save).</summary>
        public fixed int Actor[Capacity];
        /// <summary>⭐ <c>CE-11054</c> — what collapsed the building (<see cref="BreachCause.Detonation"/> today; Unknown = not collapsed or from an old save), its blast point and its explosive kg. Meaningless while <see cref="Collapsed"/> is 0.</summary>
        public byte CollapseCause;
        public float CollapseBlastX, CollapseBlastY, CollapseBlastZ;
        public float CollapseKg;

        /// <summary>The cause of breach <paramref name="i"/>.</summary>
        public readonly BreachCause CauseOf(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return (BreachCause)Cause[i];
        }

        /// <summary>The explosive kg breach <paramref name="i"/> was made with (0 = contact / unknown).</summary>
        public readonly float ExplosiveKgOf(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return ExplosiveKg[i];
        }

        /// <summary>The network id of the vehicle behind breach <paramref name="i"/> (0 = none).</summary>
        public readonly int ActorOf(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return Actor[i];
        }

        /// <summary>The collapse's blast point.</summary>
        public readonly System.Numerics.Vector3 CollapseBlast => new(CollapseBlastX, CollapseBlastY, CollapseBlastZ);

        /// <summary>Everything breach <paramref name="i"/> remembers about how it came to be.</summary>
        public readonly BreachOrigin OriginOf(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return new BreachOrigin(new SimInstant(MadeAt[i]), new System.Numerics.Vector3(BlastX[i], BlastY[i], BlastZ[i]), (BreachCause)Cause[i], ExplosiveKg[i], Actor[i]);
        }

        /// <summary>Overwrites slot <paramref name="i"/>'s whole origin (time, blast point, cause, kg, actor).</summary>
        public void SetOrigin(int i, in BreachOrigin o)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            MadeAt[i] = o.At; BlastX[i] = o.Blast.X; BlastY[i] = o.Blast.Y; BlastZ[i] = o.Blast.Z;
            Cause[i] = (byte)o.Cause; ExplosiveKg[i] = o.ExplosiveKg; Actor[i] = o.Actor;
        }

        /// <summary>Sets what collapsed the building: its cause, blast point and explosive kg.</summary>
        public void SetCollapse(BreachCause cause, System.Numerics.Vector3 blast, float kg)
        {
            CollapseCause = (byte)cause; CollapseBlastX = blast.X; CollapseBlastY = blast.Y; CollapseBlastZ = blast.Z; CollapseKg = kg;
        }

        /// <summary>When breach <paramref name="i"/> was made or last widened (0 = from a save).</summary>
        public readonly SimInstant MadeAtOf(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return (SimInstant)MadeAt[i];
        }

        /// <summary>The blast point of breach <paramref name="i"/>.</summary>
        public readonly System.Numerics.Vector3 BlastOf(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return new System.Numerics.Vector3(BlastX[i], BlastY[i], BlastZ[i]);
        }

        /// <summary>Overwrites slot <paramref name="i"/>'s time and blast point.</summary>
        public void SetStamp(int i, double at, System.Numerics.Vector3 blast)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            MadeAt[i] = at; BlastX[i] = blast.X; BlastY[i] = blast.Y; BlastZ[i] = blast.Z;
        }

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
    public static unsafe class BuildingDamages
    {
        /// <summary>⭐ The TKB type of a building-damage entity (bare: the state travels on <see cref="BuildingDamage"/> / <c>dtBuildingDamage</c>).</summary>
        public const long TkbType = 7203;

        /// <summary>The terrain-object key of building <paramref name="building"/>: <c>"&lt;terrain&gt;/&lt;label&gt;"</c> (a world with no name drops the first part).</summary>
        /// <remarks>⭐ CE-4599 (B-2) — <paramref name="building"/> is a WALL GROUP (<see cref="WallGroups"/>): past the buildings a free-wall line, keyed <c>"&lt;terrain&gt;/wall:&lt;label&gt;"</c>.</remarks>
        public static string KeyOf(TerrainWorld world, int building)
        {
            string own = WallGroups.IsBuilding(world, building) ? world.Buildings[building].Label
                : WallGroups.LineKey(world, building) ?? throw new ArgumentOutOfRangeException(nameof(building), $"no wall group {building}");
            return string.IsNullOrEmpty(world.Name) ? own : world.Name + "/" + own;
        }

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
            else if ((uint)d.Building < (uint)WallGroups.Count(from)) nb = IndexOfKey(to, KeyOf(from, d.Building));
            var n = default(BuildingDamage);
            n.Building = nb;
            if (nb >= 0)
            {
                n.Collapsed = WallGroups.IsBuilding(to, nb) ? d.Collapsed : (byte)0;
                n.CollapsedAt = n.Collapsed != 0 ? d.CollapsedAt : 0d;   // ⭐ CE-11051 — the stamps travel with what they stamp
                if (n.Collapsed != 0) n.SetCollapse((BreachCause)d.CollapseCause, d.CollapseBlast, d.CollapseKg);   // ⭐ CE-11054
                int k = 0;
                for (int i = 0; i < d.Count; i++)
                {
                    var b = d.Get(i);
                    if (BuildingPanelIndex.TryMapBreach(from, b, to, nb, out var m)) { n.SetOrigin(k, d.OriginOf(i)); n.Set(k++, m); }   // ⭐ CE-5012 — by rank, or by geometry for a re-drawn line (a gate)
                }
                n.Count = (byte)k;
            }
            bool changed = n.Building != d.Building || n.Collapsed != d.Collapsed || n.Count != d.Count || n.CollapseCause != d.CollapseCause || n.CollapseKg != d.CollapseKg;
            for (int i = 0; !changed && i < n.Count; i++) changed = n.Get(i) != d.Get(i) || n.MadeAt[i] != d.MadeAt[i] || n.Cause[i] != d.Cause[i] || n.ExplosiveKg[i] != d.ExplosiveKg[i] || n.Actor[i] != d.Actor[i];
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
                for (int g = w.Buildings.Count, n = WallGroups.Count(w); g < n; g++)   // ⭐ CE-4599 — the free-wall lines, full and bare keys
                {
                    m.TryAdd(KeyOf(w, g), g);
                    m.TryAdd(WallGroups.LineKey(w, g)!, g);
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
        public static BreachAdd Add(ref BuildingDamage d, in WallBreach b, TerrainWorld? world) => Add(ref d, in b, world, new BreachOrigin(default, default));

        /// <summary>
        /// ⭐ <c>CE-11051</c> — <see cref="Add(ref BuildingDamage, in WallBreach, TerrainWorld?)"/> that also STAMPS the breach it adds or widens: <paramref name="at"/>
        /// (sim seconds) and <paramref name="blast"/> (the blast point). The breaches it does not touch keep their stamps; an unchanged list moves nothing.
        /// </summary>
        public static BreachAdd Add(ref BuildingDamage d, in WallBreach b, TerrainWorld? world, double at, System.Numerics.Vector3 blast)
            => Add(ref d, in b, world, new BreachOrigin(new SimInstant(at), blast));

        /// <summary>
        /// ⭐ <c>CE-11054</c> — the full form: <paramref name="origin"/> (time, blast point, cause, explosive kg, actor) stamps the breach it adds or widens. ⚠ A merge is NOT a numeric max of the causes
        /// (<see cref="BreachCause"/>'s order is not a severity): <see cref="BreachCause.Detonation"/> wins when either side is one (any blast in the merged hole ⇒ scorched), else the newer breach's cause
        /// (the older one's when the newer is Unknown); the explosive kg is the larger; the actor is the newest contact's (the newer breach's when it names one, else the older's).
        /// </summary>
        public static BreachAdd Add(ref BuildingDamage d, in WallBreach b, TerrainWorld? world, in BreachOrigin origin)
        {
            int n = d.Count;
            Span<WallBreach> list = stackalloc WallBreach[BuildingDamage.Capacity + 1];
            Span<int> from = stackalloc int[BuildingDamage.Capacity + 1];   // each kept entry's slot in d (its stamp moves with it)
            for (int i = 0; i < n; i++) { list[i] = d.Get(i); from[i] = i; }

            var merged = b;
            var acc = origin;   // the merged hole's cause, kg and actor: the newer breach's, merged with every older one it absorbs
            bool absorbed = false;
            for (bool again = true; again;)
            {
                again = false;
                for (int i = 0; i < n; i++)
                {
                    if (!list[i].Overlaps(merged)) continue;
                    var kind = (BreachKind)Math.Max((byte)list[i].Kind, (byte)merged.Kind);
                    merged = list[i].UnionWith(merged, kind);
                    acc = MergeOrigin(in acc, d.OriginOf(from[i]));
                    for (int k = i + 1; k < n; k++) { list[k - 1] = list[k]; from[k - 1] = from[k]; }   // keep the order of the rest
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
            from[n] = -1;
            list[n++] = merged;
            // the origins of the kept entries move with them (read before any slot is overwritten); the new / widened one takes (time, blast point, merged cause / kg / actor)
            Span<BreachOrigin> origins = stackalloc BreachOrigin[BuildingDamage.Capacity + 1];
            for (int i = 0; i < n; i++) origins[i] = from[i] >= 0 ? d.OriginOf(from[i]) : acc;
            for (int i = 0; i < n; i++) { d.Set(i, list[i]); d.SetOrigin(i, in origins[i]); }
            for (int i = n; i < d.Count; i++) d.SetOrigin(i, default);   // slots a merge freed hold no stale stamp
            d.Count = (byte)n;
            return absorbed ? BreachAdd.Merged : BreachAdd.Added;
        }

        /// <summary>⭐ <c>CE-11054</c> — the cause / kg / actor of two breaches that become one hole (see <see cref="Add(ref BuildingDamage, in WallBreach, TerrainWorld?, in BreachOrigin)"/>); time and blast point stay the newer's.</summary>
        internal static BreachOrigin MergeOrigin(in BreachOrigin newer, in BreachOrigin older)
        {
            var cause = newer.Cause == BreachCause.Detonation || older.Cause == BreachCause.Detonation ? BreachCause.Detonation
                : newer.Cause != BreachCause.Unknown ? newer.Cause : older.Cause;
            return newer with { Cause = cause, ExplosiveKg = MathF.Max(newer.ExplosiveKg, older.ExplosiveKg), Actor = newer.Actor != 0 ? newer.Actor : older.Actor };
        }
    }
}
