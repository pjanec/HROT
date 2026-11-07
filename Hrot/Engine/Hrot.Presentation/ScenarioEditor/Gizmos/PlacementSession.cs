using System;
using System.Numerics;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.World;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ CE-4309 (Map Interaction P3, docs/UX/DESIGN_Map_Interaction.md §4 D16, Q3 / Q4) — WHERE and HOW a placed entity is born, decided while the placement
/// tool is armed. PURE (no ImGui, no bus): <c>EntityPlacementGizmo</c> feeds it the cursor, keys and the click, and builds its <c>SpawnEntityCommand</c>
/// from <see cref="Resolve"/>. Three choices:
/// <list type="bullet">
/// <item><b>airborne by type</b> — a type that FLIES (its TKB carries a flight descriptor: <see cref="AirborneHeight"/> = its <c>HoverHeightM</c>, 300 m for the
/// A-10C, 30 m for a helicopter) is placed in the air: <see cref="SpawnHeightMode.AboveLevel"/> with that height. <b>Shift</b> places it parked on the ground.
/// 📐 CE-4272 measured: an A-10C born at 300 m flies on at cruise; born on the ground it crashes.</item>
/// <item><b>the level, in 2-D</b> — where <see cref="IWorldQuery.SurfacesAt"/> stacks more than one surface under the cursor (a house's floors), PgUp / PgDn choose
/// it and the hint says <i>"level 1 of 3 · PgUp / PgDn"</i>; a 3-D click carries its own level (CE-1033) and wins.</item>
/// <item><b>the heading, by drag</b> — press, drag, release: the entity stands at the press point facing the drag; a plain click faces north (CE-1017).</item>
/// </list>
/// </summary>
public sealed class PlacementSession
{
    /// <summary>PageUp / PageDown as the map streams them (raylib key codes).</summary>
    public const int KeyPageUp = 266, KeyPageDown = 267;

    /// <summary>A press-and-release shorter than this is a click (no heading).</summary>
    public const float DragThresholdM = 3f;

    /// <summary>North: yaw +90° about Z (the transform's yaw 0 is EAST).</summary>
    public static readonly Quaternion FacingNorth = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);

    /// <summary>The height a flying type is placed at over its level (null = a ground type).</summary>
    public float? AirborneHeight { get; private set; }

    /// <summary>The 2-D level chosen under the cursor: 0 = the ground; and how many surfaces are stacked there (1 = no choice).</summary>
    public int Level { get; private set; }
    public int LevelCount { get; private set; } = 1;

    private Vector3? _pressAt;

    /// <summary>The type's flight height, or null when it does not fly. Call once the TKB template is known.</summary>
    public void SetType(float? hoverHeightM, float cruiseMps = 0f)
    {
        AirborneHeight = hoverHeightM is > 0f ? hoverHeightM : null;
        CruiseMps = cruiseMps;
    }

    /// <summary>The flying type's cruise speed (0 = not given), for the hint.</summary>
    public float CruiseMps { get; private set; }

    /// <summary>The cursor moved (2-D: Z = 0): re-reads the surfaces stacked under it; the chosen level is kept while it still exists.</summary>
    public void Hover(IWorldQuery? world, Vector3 cursor)
    {
        if (world == null || cursor.Z != 0f) { LevelCount = 1; Level = 0; return; }
        var surfaces = world.SurfacesAt(cursor.X, cursor.Y, out int ground);
        int above = Math.Max(0, surfaces.Count - 1 - ground);
        LevelCount = above + 1;
        if (Level > above) Level = above;
    }

    /// <summary>PgUp / PgDn: one level up / down. True when the key was one of them.</summary>
    public bool Key(int key)
    {
        if (key == KeyPageUp) { if (Level < LevelCount - 1) Level++; return true; }
        if (key == KeyPageDown) { if (Level > 0) Level--; return true; }
        return false;
    }

    /// <summary>The left button went down at <paramref name="at"/> (the start of a possible heading drag).</summary>
    public void Press(Vector3 at) => _pressAt = at;

    /// <summary>The two lines under the ghost, worded as the mockup (map-interaction-visual.svg C / D): an aircraft's height and speed and Shift; the level choice
    /// and the drag; else <paramref name="baseHint"/> alone (second line null).</summary>
    public (string First, string? Second) Hint(string baseHint)
    {
        if (AirborneHeight is { } h)
            return (CruiseMps > 0f ? $"airborne · {h:0} m over the ground · {CruiseMps:0} m/s" : $"airborne · {h:0} m over the ground", "Shift: parked on the ground");
        if (LevelCount > 1) return ($"level {Level} of {LevelCount - 1}   PgUp / PgDn", "drag: heading · right-click / Esc: done");
        return (baseHint, null);
    }

    /// <summary>
    /// The click released at <paramref name="at"/> (<paramref name="shift"/> held?): the position to send, the birth height and the facing.
    /// <paramref name="levelOf3D"/> is the level a 3-D click hit (null for a 2-D click).
    /// </summary>
    /// <summary>⭐ A5 (📄 docs/UX/DESIGN_Entity_Copy_Paste.md §3) — where a release at <paramref name="cursor"/> WOULD place, and the drag's heading (math yaw,
    /// radians; null = no drag past the threshold): the press point while dragging, else the cursor. PURE (it reads the press, never clears it), so the
    /// preview a tool draws each frame and the release agree by construction.</summary>
    public (Vector3 At, float? DragYawRad) Pending(Vector3 cursor)
    {
        if (_pressAt is { } p)
        {
            var d = new Vector2(cursor.X - p.X, cursor.Y - p.Y);
            if (d.Length() >= DragThresholdM) return (p, MathF.Atan2(d.Y, d.X));
        }
        return (cursor, null);
    }

    /// <summary>Forgets a press (a cancelled tool).</summary>
    public void ClearPress() => _pressAt = null;

    public (Vector3 Position, SpawnHeight Height, Quaternion Rotation) Resolve(Vector3 at, bool shift, int? levelOf3D)
    {
        var place = at;
        var rotation = FacingNorth;
        if (_pressAt is { } p)
        {
            var d = new Vector2(at.X - p.X, at.Y - p.Y);
            if (d.Length() >= DragThresholdM) { place = p; rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.Atan2(d.Y, d.X)); }
        }
        _pressAt = null;
        short level = (short)(levelOf3D ?? Level);
        if (AirborneHeight is { } h && !shift)
            return (new Vector3(place.X, place.Y, h), new SpawnHeight(SpawnHeightMode.AboveLevel, level), rotation);
        var height = level == 0 ? SpawnHeight.OnGround : new SpawnHeight(SpawnHeightMode.OnLevel, level);
        return (new Vector3(place.X, place.Y, 0f), height, rotation);
    }
}
