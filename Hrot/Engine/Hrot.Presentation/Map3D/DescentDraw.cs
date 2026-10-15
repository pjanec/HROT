using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Motion;

namespace Hrot.UI.Common.Map3D;

/// <summary>⭐ CE-1119 (docs/DESIGN_Map_3D_Mode.md §6w AB-V2) — the figures a descent is drawn with.</summary>
public enum DescentPose : byte { Tuck, RopeSlide, Arch, UnderCanopy, Flare, Rolled, SittingOut }

/// <summary>⭐ CE-1119 — one piece of a canopy: a unit mesh (a centred unit cube, or a cone / cylinder running along +Z from 0 to 1 with
/// radius 0.5 — the kit convention) and where it goes in the jumper's BODY frame (metres).</summary>
public readonly record struct CanopyPiece(PartShape Shape, Matrix4x4 Local);

/// <summary>
/// ⭐ CE-1119 (docs/DESIGN_Map_3D_Mode.md §6w; docs/designs/airborne/DESIGN_Airborne_Insertion.md U-1) — a replicated <see cref="Descent"/> as
/// something to draw, pure so the rails need no window: the figure for its phase (blended from the phase before), the canopy (round or
/// ram-air, opening while it deploys, collapsing as he lands) with its lines, in his BODY frame so it banks with his roll (Airborne D-5), and
/// the rope's ends. <c>EntityBodyLayer3D</c> places what this returns.
/// </summary>
public static class DescentDraw
{
    /// <summary>How long one phase's figure takes to become the next (s, sim time); the landing roll takes <see cref="RollSeconds"/>, the
    /// canopy <see cref="CollapseSeconds"/> to fall flat behind him.</summary>
    public const float BlendSeconds = 0.3f, RollSeconds = 0.6f, CollapseSeconds = 1.2f;

    /// <summary>The round canopy (T-11 class): 10 m across, its skirt 6.5 m above his shoulders. The ram-air (MC-4 class): 8.5 m span,
    /// 3.4 m chord, 4.8 m above his shoulders.</summary>
    public const float RoundRadius = 5f, RoundLines = 6.5f, RamAirSpan = 8.5f, RamAirChord = 3.4f, RamAirAbove = 4.8f;

    /// <summary>The rope's thickness (m) — a fast rope is ~5 cm of braided nylon.</summary>
    public const float RopeThickness = 0.05f;

    /// <summary>Where the risers leave his shoulders, and how far above them the lines meet the risers (m, body frame).</summary>
    public static readonly Vector3 ShoulderL = new(0f, 0.26f, 1.43f), ShoulderR = new(0f, -0.26f, 1.43f);
    public const float RiserLength = 0.55f;

    /// <summary>How far behind him a canopy that came down lies (m) — the length of its lines, strung out on the ground.</summary>
    public const float CollapsedBehind = 6f;

    /// <summary>The figure for a phase; null when he is simply a man on his feet again (off the rope).</summary>
    public static DescentPose? PoseOf(DescentKind kind, DescentPhase phase)
    {
        if (kind == DescentKind.FastRope)
            return phase is DescentPhase.Landed ? null : phase == DescentPhase.Falling ? DescentPose.Arch : DescentPose.RopeSlide;
        return phase switch
        {
            DescentPhase.Exit => kind == DescentKind.FreeFall ? DescentPose.Arch : DescentPose.Tuck,
            DescentPhase.FreeFalling or DescentPhase.Falling => DescentPose.Arch,
            DescentPhase.Deploying => DescentPose.UnderCanopy,
            DescentPhase.Flare => DescentPose.Flare,
            DescentPhase.Landing => DescentPose.Rolled,
            DescentPhase.Landed => DescentPose.SittingOut,
            _ => DescentPose.UnderCanopy,
        };
    }

    /// <summary>The figure of the phase before <paramref name="phase"/> (what it blends from), or null.</summary>
    public static DescentPose? Before(DescentKind kind, DescentPhase phase) => phase switch
    {
        DescentPhase.Deploying => kind == DescentKind.FreeFall ? DescentPose.Arch : DescentPose.Tuck,
        DescentPhase.Canopy => DescentPose.UnderCanopy,
        DescentPhase.Flare => DescentPose.UnderCanopy,
        DescentPhase.Landing => kind == DescentKind.FreeFall ? DescentPose.Flare : DescentPose.UnderCanopy,
        DescentPhase.Landed => DescentPose.Rolled,
        DescentPhase.Falling => kind == DescentKind.FastRope ? DescentPose.RopeSlide : DescentPose.UnderCanopy,
        _ => null,
    };

    /// <summary>
    /// The figure of <paramref name="d"/> at <paramref name="now"/> (sim time): the phase's pose, blended in from the one before it over
    /// <see cref="BlendSeconds"/> — over the whole opening (<paramref name="openSeconds"/>) while the canopy deploys, over
    /// <see cref="RollSeconds"/> for the landing roll. Null for a man off the rope.
    /// </summary>
    public static KitPart[]? Figure(in Descent d, double now, bool soldier, float openSeconds)
    {
        if (PoseOf(d.Kind, d.Phase) is not { } pose) return null;
        var parts = BlockFigure.Descending(pose, soldier);
        if (Before(d.Kind, d.Phase) is not { } before || before == pose) return parts;
        float since = (float)Math.Max(0.0, now - d.PhaseSince);
        float over = d.Phase switch
        {
            DescentPhase.Deploying => MathF.Max(0.2f, openSeconds),
            DescentPhase.Landing => RollSeconds,
            _ => BlendSeconds,
        };
        float t = Math.Clamp(since / over, 0f, 1f);
        return t >= 1f ? parts : BlockFigure.BlendDescent(BlockFigure.Descending(before, soldier), parts, t * t * (3f - 2f * t));
    }

    /// <summary>How open the canopy is (0..1), or −1 when there is none out (a rope, the exit, free fall, a man who lost it).</summary>
    public static float Opening(in Descent d, double now, float openSeconds)
    {
        if (d.Kind == DescentKind.FastRope || d.Canopy == CanopyKind.NoCanopy) return -1f;
        return d.Phase switch
        {
            DescentPhase.Exit or DescentPhase.FreeFalling or DescentPhase.Falling => -1f,
            DescentPhase.Deploying => Math.Clamp((float)(now - d.PhaseSince) / MathF.Max(0.2f, openSeconds), 0.05f, 1f),
            _ => 1f,
        };
    }

    /// <summary>How far the canopy has fallen flat behind him (0 flying … 1 lying on the ground): over <see cref="CollapseSeconds"/> of the
    /// landing, then lying there.</summary>
    public static float Collapse(in Descent d, double now) => d.Phase switch
    {
        DescentPhase.Landing => Math.Clamp((float)(now - d.PhaseSince) / CollapseSeconds, 0f, 1f),
        DescentPhase.Landed => 1f,
        _ => 0f,
    };

    /// <summary>
    /// The canopy's pieces and its lines (risers from his shoulders, then the lines to the canopy), in his body frame. A round canopy is a
    /// dome with a skirt and eight lines; a ram-air wing three cells arched down at the tips with three lines a side. <paramref name="opening"/>
    /// grows it from a thin streamer; <paramref name="collapse"/> drops it flat on the ground behind him (no lines once it lies there).
    /// </summary>
    public static void Canopy(CanopyKind kind, float opening, float collapse, List<CanopyPiece> pieces, List<(Vector3 A, Vector3 B)> lines)
    {
        pieces.Clear();
        lines.Clear();
        if (kind == CanopyKind.NoCanopy || opening < 0f) return;
        float o = Math.Clamp(opening, 0f, 1f), c = Math.Clamp(collapse, 0f, 1f);
        o = o * o * (3f - 2f * o);
        var riserL = ShoulderL + new Vector3(0f, 0f, RiserLength);
        var riserR = ShoulderR + new Vector3(0f, 0f, RiserLength);
        if (c < 1f) { lines.Add((ShoulderL, riserL)); lines.Add((ShoulderR, riserR)); }

        if (c >= 1f)   // lying on the ground behind him: the fabric in a low heap at the end of the strung-out lines
        {
            bool ram = kind == CanopyKind.RamAir;
            pieces.Add(new(PartShape.Cone, Matrix4x4.CreateScale(ram ? 2.6f : 3.2f, ram ? 1.8f : 3.2f, 0.5f) * Matrix4x4.CreateTranslation(-CollapsedBehind, 0f, 0f)));
            pieces.Add(new(PartShape.Box, Matrix4x4.CreateScale(CollapsedBehind - 1.2f, ram ? 1.0f : 1.4f, 0.10f) * Matrix4x4.CreateTranslation(-(CollapsedBehind + 1.2f) / 2f, 0f, 0.05f)));
            return;
        }
        if (kind == CanopyKind.Round)
        {
            // collapsing (the landing): it shrinks and drops behind him onto the ground, the lines still to its edge
            float r = Lerp(Lerp(0.3f, RoundRadius, o), 1.6f, c), h = Lerp(Lerp(3.5f, 2.4f, o), 0.5f, c);
            float skirt = Lerp(ShoulderL.Z + Lerp(4.0f, RoundLines, o), 0.4f, c);
            var at = new Vector3(-CollapsedBehind * c, 0f, skirt);
            float band = 0.4f * (1f - 0.7f * c);
            pieces.Add(new(PartShape.Cone, Matrix4x4.CreateScale(2f * r, 2f * r, h) * Matrix4x4.CreateTranslation(at)));
            pieces.Add(new(PartShape.Cylinder, Matrix4x4.CreateScale(2f * r, 2f * r, band) * Matrix4x4.CreateTranslation(at - new Vector3(0f, 0f, band))));
            for (int k = 0; k < 8; k++)
            {
                float a = (k + 0.5f) * MathF.PI / 4f;
                var edge = at + new Vector3(r * MathF.Cos(a), r * MathF.Sin(a), -band);
                lines.Add((edge.Y >= 0f ? riserL : riserR, edge));
            }
            return;
        }

        // ram-air: a wing, its leading edge forward (+X)
        float b = Lerp(Lerp(1.2f, RamAirSpan, o), 3.0f, c), chord = Lerp(Lerp(1.0f, RamAirChord, o), 1.6f, c), thick = 0.45f * (1f - 0.6f * c);
        float zc = Lerp(ShoulderL.Z + Lerp(3.0f, RamAirAbove, o), 0.3f, c);
        var centre = new Vector3(Lerp(0.3f, -CollapsedBehind, c), 0f, zc);
        float arch = 20f * MathF.PI / 180f * (1f - c), half = b / 4f, tip = b / 4f;
        pieces.Add(new(PartShape.Box, Matrix4x4.CreateScale(chord, 2f * half, thick) * Matrix4x4.CreateTranslation(centre)));
        for (int side = -1; side <= 1; side += 2)
        {
            var mid = centre + new Vector3(0f, side * (half + tip / 2f * MathF.Cos(arch)), -tip / 2f * MathF.Sin(arch));
            pieces.Add(new(PartShape.Box, Matrix4x4.CreateScale(chord, tip, thick * 0.9f) * Matrix4x4.CreateRotationX(-side * arch) * Matrix4x4.CreateTranslation(mid)));
            var riser = side > 0 ? riserL : riserR;
            float under = -thick / 2f;
            lines.Add((riser, centre + new Vector3(chord / 3f, side * half * 0.6f, under)));
            lines.Add((riser, centre + new Vector3(-chord / 3f, side * half * 0.6f, under)));
            lines.Add((riser, mid + new Vector3(0f, side * tip * 0.4f * MathF.Cos(arch), under - tip * 0.4f * MathF.Sin(arch))));
        }
    }

    /// <summary>A rope hanging from <paramref name="top"/>: its foot on the surface under it when it is long enough (the spare lying coiled
    /// there), else hanging free at its full <paramref name="length"/>.</summary>
    public static (Vector3 Bottom, float Spare) Rope(Vector3 top, float surfaceZ, float length)
    {
        float drop = top.Z - surfaceZ;
        if (drop <= 0f) return (top, length);
        return drop <= length ? (new Vector3(top.X, top.Y, surfaceZ), length - drop) : (top - new Vector3(0f, 0f, length), 0f);
    }

    /// <summary>A unit cube stretched from <paramref name="a"/> to <paramref name="b"/>, <paramref name="width"/> square across — a rope, a line.</summary>
    public static Matrix4x4 Stick(Vector3 a, Vector3 b, float width)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 1e-5f) return Matrix4x4.CreateScale(1e-4f) * Matrix4x4.CreateTranslation(a);
        var dir = d / len;
        var axis = Vector3.Cross(Vector3.UnitX, dir);
        float dot = Math.Clamp(Vector3.Dot(Vector3.UnitX, dir), -1f, 1f);
        var turn = axis.LengthSquared() < 1e-10f
            ? (dot > 0f ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI))
            : Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(dot));
        return Matrix4x4.CreateScale(len, width, width) * Matrix4x4.CreateFromQuaternion(turn) * Matrix4x4.CreateTranslation((a + b) / 2f);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
