using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Replication.Edits;
using Hrot.ScenarioEditor.Gizmos;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐ CE-4647 (AC-15 step 6; DESIGN_Air_Combat §3 AC-10, mockup <c>air-combat-arena.svg</c>) — the arena form's and the gizmo's WRITES on the air lane's <see cref="AirArena"/> (component 160). The MinefieldOps shape: every write is ONE edit step (the editor's history
/// snapshots the world round the bracket), a no-op when it changes nothing, everything validated by the ONE <see cref="AirArenaGizmo.Clamp"/> so the form and the gizmo cannot disagree. No caches, no statics keyed by entity.
/// </summary>
public static class AirArenaOps
{
    /// <summary>The show-rule ranges the form accepts: the shot range (m) and the stalemate time (s).</summary>
    public const float MinShotRange = 500f, MaxShotRange = 30000f, MinStalemate = 10f, MaxStalemate = 3600f;

    /// <summary>The numbers a fresh arena is born with (the contract's defaults).</summary>
    public static AirArena Default() => new()
    {
        RadiusM = AirArena.DefaultRadiusM, FloorAglM = AirArena.DefaultFloorAglM, CeilingAglM = AirArena.DefaultCeilingAglM,
        MaxShotRangeM = AirArena.DefaultMaxShotRangeM, StalemateSeconds = AirArena.DefaultStalemateSeconds,
    };

    /// <summary>True when <paramref name="e"/> is a live air arena.</summary>
    public static bool Is(EntityRepository w, Entity e) => w.IsAlive(e) && w.IsComponentTypeRegistered<AirArena>() && w.HasComponent<AirArena>(e);

    /// <summary>The four numbers the gizmo draws and drags (the centre's Z is unused).</summary>
    public static ArenaShape ShapeOf(in AirArena a) => new(new Vector2(a.Centre.X, a.Centre.Y), a.RadiusM, a.FloorAglM, a.CeilingAglM);

    public static bool Same(in AirArena a, in AirArena b)
    {
        var x = a; var y = b;
        return MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref x, 1)).SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref y, 1)));
    }

    private delegate void Change(ref AirArena a);

    private static bool Edit(EntityRepository w, Entity e, string label, Change change)
    {
        if (!Is(w, e)) return false;
        var after = w.GetComponentRO<AirArena>(e);
        var before = after;
        change(ref after);
        if (Same(in before, in after)) return false;
        EditSteps.Begin(w, label);
        try { w.GetComponentRW<AirArena>(e) = after; }
        finally { EditSteps.End(w); }
        return true;
    }

    private static void Put(ref AirArena a, in ArenaShape s) { var c = AirArenaGizmo.Clamp(in s); a.Centre = new Vector3(c.Centre.X, c.Centre.Y, a.Centre.Z); a.RadiusM = c.Radius; a.FloorAglM = c.FloorM; a.CeilingAglM = c.CeilingM; }

    /// <summary>The whole shape at once (a gizmo drag's end): ONE step.</summary>
    public static bool SetShape(EntityRepository w, Entity e, in ArenaShape shape)
    {
        var s = shape;
        return Edit(w, e, "Edit air arena", (ref AirArena a) => Put(ref a, in s));
    }

    public static bool SetCentre(EntityRepository w, Entity e, Vector2 centre)
        => float.IsFinite(centre.X) && float.IsFinite(centre.Y) && Edit(w, e, "Move air arena", (ref AirArena a) => Put(ref a, ShapeOf(in a) with { Centre = centre }));

    public static bool SetRadius(EntityRepository w, Entity e, float metres)
        => !float.IsNaN(metres) && Edit(w, e, "Set arena radius", (ref AirArena a) => Put(ref a, ShapeOf(in a) with { Radius = metres }));

    public static bool SetFloor(EntityRepository w, Entity e, float metres)
        => !float.IsNaN(metres) && Edit(w, e, "Set arena floor", (ref AirArena a) => Put(ref a, ShapeOf(in a) with { FloorM = metres }));

    public static bool SetCeiling(EntityRepository w, Entity e, float metres)
        => !float.IsNaN(metres) && Edit(w, e, "Set arena ceiling", (ref AirArena a) => Put(ref a, ShapeOf(in a) with { CeilingM = metres }));

    public static bool SetMaxShotRange(EntityRepository w, Entity e, float metres)
        => !float.IsNaN(metres) && Edit(w, e, "Set arena shot range", (ref AirArena a) => a.MaxShotRangeM = Math.Clamp(metres, MinShotRange, MaxShotRange));

    public static bool SetStalemate(EntityRepository w, Entity e, float seconds)
        => !float.IsNaN(seconds) && Edit(w, e, "Set arena stalemate time", (ref AirArena a) => a.StalemateSeconds = Math.Clamp(seconds, MinStalemate, MaxStalemate));

    public static bool SetLapsesOff(EntityRepository w, Entity e, bool off)
        => Edit(w, e, off ? "Arena: no lapses" : "Arena: lapses on", (ref AirArena a) => a.LapsesOff = off ? (byte)1 : (byte)0);
}
