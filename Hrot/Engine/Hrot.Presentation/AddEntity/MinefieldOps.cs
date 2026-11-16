using System.Globalization;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using System.Numerics;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐ CE-4624 (minefields MS-U3, DESIGN_Minefields MR-8, MR-18, MF-12) — the minefield form's WRITES on terrain's <see cref="Minefield"/>: type, spacing, row orientation, seed, anti-handling, the sides that know it at
/// start, and the LANES (add — refused at <see cref="Minefield.MaxLanes"/> — and delete, the rest compacted). Every write is ONE edit step (the editor's history snapshots the world round the bracket) and a no-op when it changes
/// nothing, like <see cref="AnimalAreaOps"/>. The OUTLINE is the entity's one <c>EditablePolyline</c> (MR-8) — never here. Also the two reads the form and the vertex gizmo need: <see cref="ShapeLock"/> (a field with a mine
/// gone off may not change its outline) and <see cref="Counts"/> (the derived mine counts, cached per field: 0 B on a steady frame).
/// </summary>
public static class MinefieldOps
{
    /// <summary>The spacing's range (m): 2 … 100 (the pattern guards a huge field at a tiny spacing; a mine spacing of metres is a real minefield).</summary>
    public const float MinSpacing = 2f, MaxSpacing = 100f;

    /// <summary>The width a new lane is given (m) — a mine-clearing vehicle's swath (MR-18).</summary>
    public const float DefaultLaneWidth = 4.5f;

    /// <summary>The numbers a fresh field is born with (the area tool's <c>BornWith</c>): mixed, 10 m, rows east, seed 1.</summary>
    public static Minefield Default() => new() { Type = MineType.Mixed, SpacingM = 10f, OrientationDeg = 0f, Seed = 1 };

    /// <summary>True when <paramref name="e"/> is a live minefield.</summary>
    public static bool Is(EntityRepository w, Entity e) => w.IsAlive(e) && w.IsComponentTypeRegistered<Minefield>() && w.HasComponent<Minefield>(e);

    public static bool Same(in Minefield a, in Minefield b)
    {
        var x = a; var y = b;
        return MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref x, 1)).SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref y, 1)));
    }

    private delegate void Change(ref Minefield f);

    private static bool Edit(EntityRepository w, Entity e, string label, Change change)
    {
        if (!Is(w, e)) return false;
        var after = w.GetComponentRO<Minefield>(e);
        var before = after;
        change(ref after);
        if (Same(in before, in after)) return false;
        EditSteps.Begin(w, label);
        try { w.GetComponentRW<Minefield>(e) = after; }
        finally { EditSteps.End(w); }
        return true;
    }

    public static bool SetType(EntityRepository w, Entity e, MineType type)
        => type is MineType.AntiPersonnel or MineType.AntiTank or MineType.Mixed && Edit(w, e, "Set minefield type", (ref Minefield f) => f.Type = type);

    /// <summary>Spacing, kept in <see cref="MinSpacing"/> … <see cref="MaxSpacing"/> (a NaN is refused).</summary>
    public static bool SetSpacing(EntityRepository w, Entity e, float metres)
        => !float.IsNaN(metres) && Edit(w, e, "Set minefield spacing", (ref Minefield f) => f.SpacingM = Math.Clamp(metres, MinSpacing, MaxSpacing));

    /// <summary>The rows' bearing, wrapped into [0, 360).</summary>
    public static bool SetOrientation(EntityRepository w, Entity e, float degrees)
        => float.IsFinite(degrees) && Edit(w, e, "Set minefield row orientation", (ref Minefield f) => f.OrientationDeg = Wrap(degrees));

    public static float Wrap(float degrees) { float d = degrees % 360f; if (d < 0f) d += 360f; return d >= 360f ? 0f : d; }

    public static bool SetSeed(EntityRepository w, Entity e, uint seed) => Edit(w, e, "Set minefield seed", (ref Minefield f) => f.Seed = seed);

    /// <summary>The seed re-rolled (a different jitter, the same field): a varied but deterministic next seed — never 0 (0 = the exact lattice).</summary>
    public static bool Reroll(EntityRepository w, Entity e)
        => Edit(w, e, "Re-roll minefield", (ref Minefield f) => { uint s = f.Seed * 1664525u + 1013904223u; f.Seed = s == 0 ? 1u : s; });

    public static bool SetAntiHandling(EntityRepository w, Entity e, bool on) => Edit(w, e, on ? "Fit anti-handling devices" : "Remove anti-handling devices", (ref Minefield f) => f.AntiHandling = on ? (byte)1 : (byte)0);

    /// <summary>The side (a <c>ForceId</c>, 0 … 15) that knows the field at start, on or off.</summary>
    public static bool SetKnownAtStart(EntityRepository w, Entity e, int side, bool on)
        => (uint)side < 16 && Edit(w, e, on ? "Field known at start" : "Field not known at start", (ref Minefield f) =>
        {
            ushort bit = (ushort)(1 << side);
            f.KnownAtStartSides = on ? (ushort)(f.KnownAtStartSides | bit) : (ushort)(f.KnownAtStartSides & ~bit);
        });

    /// <summary>A new lane A–B of <paramref name="width"/> (default 4.5 m); false at <see cref="Minefield.MaxLanes"/> or for a degenerate segment.</summary>
    public static bool AddLane(EntityRepository w, Entity e, Vector2 a, Vector2 b, float width = DefaultLaneWidth)
        => Vector2.DistanceSquared(a, b) > 0.01f && Edit(w, e, "Add lane", (ref Minefield f) =>
        {
            if (f.LaneCount >= Minefield.MaxLanes) return;
            f.SetLane(f.LaneCount, new MinefieldLane(a, b, Math.Max(0.5f, width), 0, 0));
            f.LaneCount++;
        });

    /// <summary>Lane <paramref name="index"/> removed, the later lanes moved up.</summary>
    public static bool DeleteLane(EntityRepository w, Entity e, int index)
        => Edit(w, e, "Delete lane", (ref Minefield f) =>
        {
            if ((uint)index >= f.LaneCount) return;
            for (int i = index; i < f.LaneCount - 1; i++) f.SetLane(i, f.Lane(i + 1));
            f.SetLane(f.LaneCount - 1, default);
            f.LaneCount--;
        });

    // ── the shape lock (MR-8) ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ MR-8 — true when a mine of the field has GONE OFF (a <see cref="MinefieldStrikes"/> record whose <c>Field</c> is the field's <c>EntityGuid</c> holds a Struck entry): the outline may no longer change, because a
    /// struck (row, col) would silently move or vanish. <paramref name="reason"/> says so (invariant culture). Type, spacing and lanes stay editable.
    /// </summary>
    public static bool ShapeLock(EntityRepository w, Entity field, out string reason)
    {
        reason = "";
        int n = StruckCount(w, field);
        if (n <= 0) return false;
        reason = string.Create(CultureInfo.InvariantCulture, $"Shape locked: {n} mine(s) have gone off in this field - delete and re-lay it to change the outline");
        return true;
    }

    /// <summary>How many Struck entries the strikes of <paramref name="field"/> hold (0 when none or the strikes are not registered).</summary>
    public static int StruckCount(EntityRepository w, Entity field)
    {
        Tally(w, field, out int struck, out _);
        return struck;
    }

    private static void Tally(EntityRepository w, Entity field, out int struck, out int cleared)
    {
        struck = 0; cleared = 0;
        if (!w.IsAlive(field) || !w.IsComponentTypeRegistered<MinefieldStrikes>() || !w.IsComponentTypeRegistered<EntityGuid>() || !w.HasComponent<EntityGuid>(field)) return;
        var guid = w.GetComponentRO<EntityGuid>(field).Value;
        var st = StateOf(w);
        lock (st)
        foreach (var e in st.Strikes ??= w.Query().With<MinefieldStrikes>().Build())
        {
            ref readonly var s = ref w.GetComponentRO<MinefieldStrikes>(e);
            if (s.Field != guid) continue;
            for (int i = 0; i < s.Count && i < MinefieldStrikes.Capacity; i++)
            {
                var how = s.Get(i).How;
                if (how == MineGone.Struck) struck++; else if (how == MineGone.Cleared) cleared++;
            }
        }
    }

    /// <summary>The hook <see cref="Hrot.ScenarioEditor.Gizmos.OutlineEditPolicy"/> asks: the lock reason of a minefield's outline, else null.</summary>
    public static string? OutlineRefusal(EntityRepository w, Entity e) => Is(w, e) && ShapeLock(w, e, out var reason) ? reason : null;

    // ── the derived counts (MF-2): cached per field, rebuilt only when it changes ───────────────────────────────────────────────

    /// <summary>The mines of a field: all of them as laid, by type, the ones that have gone off, and the cleared ones (by a lane's geometry or listed as cleared).</summary>
    public readonly record struct MineCounts(int Total, int AntiPersonnel, int AntiTank, int Struck, int Cleared);

    private sealed class CountCache
    {
        public Minefield Params; public object? Source; public Vector2 Anchor; public int Struck, Cleared;
        public MineCounts Counts;
    }

    /// <summary>⭐ The caches belong to ONE world (a static keyed by entity alone mixed worlds — the editor's UI world and its sim world, or parallel test worlds —
    /// and raced: a red only in a full run). Per world, behind its own lock (uncontended on the presentation thread, 0 B).</summary>
    private sealed class WorldState
    {
        public readonly Dictionary<Entity, CountCache> Caches = new();
        public readonly List<Vector2> Outline = new();
        public readonly List<Mine> Scratch = new();
        public EntityQuery? Strikes;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, WorldState> States = new();

    private static WorldState StateOf(EntityRepository w) => States.GetValue(w, static _ => new WorldState());

    /// <summary>
    /// The derived counts of <paramref name="field"/> (zeros when it has no outline). ⭐ Cold: rebuilt only when its parameters, its outline instance, its position or its strikes changed; a steady frame
    /// reads the cache (0 B — the strikes are tallied through a cached query, no list is made).
    /// </summary>
    public static MineCounts Counts(EntityRepository w, Entity field)
    {
        if (!Is(w, field) || !w.HasComponent<SimTransform>(field)) return default;
        var source = w.GetManagedComponentByTypeId(field, GlobalComponentIds.EditablePolyline);
        var at = w.GetComponentRO<SimTransform>(field).Position;
        var anchor = new Vector2(at.X, at.Y);
        Tally(w, field, out int struck, out int clearedListed);
        ref readonly var f = ref w.GetComponentRO<Minefield>(field);
        var st = StateOf(w);
        lock (st) return CountsLocked(w, field, st, source, anchor, struck, clearedListed, in f);
    }

    private static MineCounts CountsLocked(EntityRepository w, Entity field, WorldState st, object? source, Vector2 anchor, int struck, int clearedListed, in Minefield f)
    {
        var Caches = st.Caches; var Outline = st.Outline; var Scratch = st.Scratch;
        if (Caches.TryGetValue(field, out var c) && ReferenceEquals(c.Source, source) && c.Anchor == anchor && c.Struck == struck && c.Cleared == clearedListed && Same(in c.Params, in f)) return c.Counts;
        c ??= new CountCache();
        Caches[field] = c;
        c.Params = f; c.Source = source; c.Anchor = anchor; c.Struck = struck; c.Cleared = clearedListed;
        c.Counts = default;
        if (Caches.Count > 256) PruneDead(w, Caches);
        if (MinefieldKnowledgeOps.TryOutline(w, field, Outline, out var a))
        {
            var bare = f; bare.LaneCount = 0;
            Scratch.Clear(); MinePattern.MinesIn(in bare, a, Outline, Scratch);   // as laid: lanes ignored
            int all = Scratch.Count, ap = 0, at2 = 0;
            for (int i = 0; i < Scratch.Count; i++) if (Scratch[i].Type == MineType.AntiPersonnel) ap++; else if (Scratch[i].Type == MineType.AntiTank) at2++;
            Scratch.Clear(); MinePattern.MinesIn(in f, a, Outline, Scratch);       // lanes excluded
            int laneCleared = all - Scratch.Count;
            Scratch.Clear();
            c.Counts = new MineCounts(all, ap, at2, struck, clearedListed + laneCleared);
        }
        return c.Counts;
    }

    private static void PruneDead(EntityRepository w, Dictionary<Entity, CountCache> Caches)
    {
        List<Entity>? dead = null;
        foreach (var k in Caches.Keys) if (!w.IsAlive(k)) (dead ??= new()).Add(k);
        if (dead != null) foreach (var k in dead) Caches.Remove(k);
    }

    /// <summary>The length of lane <paramref name="i"/> in metres.</summary>
    public static float LaneLength(in Minefield f, int i) { var l = f.Lane(i); return Vector2.Distance(l.A, l.B); }
}
