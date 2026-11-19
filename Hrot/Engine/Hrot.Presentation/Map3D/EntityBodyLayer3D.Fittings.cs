using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Look.Vehicles;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-4670</c> (📄 docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §7d; mockup docs/UX/vehicle-openings-lights.svg) — a vehicle's FITTINGS on the
/// 3-D map, from its replicated state (a late joiner and a replay draw the same): every opening of its type as a lid / ramp / door hinged where
/// <see cref="VehicleSockets"/> puts it, turned by <see cref="VehicleLook.OpeningProgress"/> (a turret one with the turret); a crewman's head and shoulders out of a
/// <see cref="VehicleLook.CrewExposed"/> hatch; and the lit light groups (<see cref="VehicleLook.Lights"/> with this layer's ONE <see cref="BrakeLights"/>) as small
/// unlit glowing points, drawn after the lit batch. 0 B steady: the boxes are cached per kit, the light list is a reused buffer.
/// </summary>
public sealed partial class EntityBodyLayer3D
{
    private readonly BrakeLights _brakes = new();
    private readonly Dictionary<(KitPart[] Parts, Vector3 Size), VehicleBoxes> _boxes = new();
    private readonly List<(Vector3 At, Color Colour)> _lightsNow = new();

    /// <summary>The crew's dark green and a face's skin, for a man out of a hatch.</summary>
    public static readonly Color CrewUniform = new(63, 74, 46, 255), CrewSkin = new(217, 169, 130, 255);

    /// <summary>Openings drawn / crewmen out of a hatch / light points drawn in the last 3-D frame — the rails' counters.</summary>
    public int OpeningsDrawn { get; private set; }
    /// <inheritdoc cref="OpeningsDrawn"/>
    public int CrewOutDrawn { get; private set; }
    /// <inheritdoc cref="OpeningsDrawn"/>
    public int LightsDrawn { get; private set; }

    /// <summary>
    /// The hull, turret and cab boxes of a kit (body metres), cached per kit array and size: the hull the bounds of its PAINTED hull parts (not the tracks, the
    /// wheels, the gear, the gun), the turret the bounds of its turret parts, the cab its first glass part.
    /// </summary>
    internal VehicleBoxes BoxesOf(KitPart[] parts, Vector3 size)
    {
        if (_boxes.TryGetValue((parts, size), out var cached)) return cached;
        Vector3 hMin = new(float.MaxValue), hMax = new(float.MinValue), tMin = hMin, tMax = hMax;
        BodyBox? cab = null;
        bool turret = false;
        foreach (var p in parts)
        {
            var c = p.Centre * size;
            var half = p.Extent * size * 0.5f;
            if (p.Shape != PartShape.Box)   // a cylinder / cone: its extent is (diameter, diameter, length) along its axis — a fair box either way
                half = new Vector3(MathF.Max(half.X, half.Z), MathF.Max(half.Y, half.Z), half.Z);
            if (p.Role == PartRole.Turret) { tMin = Vector3.Min(tMin, c - half); tMax = Vector3.Max(tMax, c + half); turret = true; }
            else if (p.Role == PartRole.Hull && p.Colour == ColourRole.Body) { hMin = Vector3.Min(hMin, c - half); hMax = Vector3.Max(hMax, c + half); }   // the painted hull, not the tracks / wheels
            if (cab == null && p.Colour == ColourRole.Glass) cab = new BodyBox(c, half);
        }
        if (hMin.X > hMax.X) { hMin = Vector3.Zero; hMax = size; hMin.X = -size.X / 2f; hMax.X = size.X / 2f; hMin.Y = -size.Y / 2f; hMax.Y = size.Y / 2f; }
        var boxes = new VehicleBoxes(new BodyBox((hMin + hMax) * 0.5f, (hMax - hMin) * 0.5f),
                                     turret ? new BodyBox((tMin + tMax) * 0.5f, (tMax - tMin) * 0.5f) : null, cab);
        _boxes[(parts, size)] = boxes;
        return boxes;
    }

    /// <summary>
    /// ⭐ CE-4670 — draw <paramref name="e"/>'s openings and collect its lit lights (drawn after the batch). <paramref name="kitToBody"/> × <paramref name="body"/> places
    /// the kit; a turret socket also turns by the turret part's pose against its neutral one.
    /// </summary>
    private void DrawFittings(EntityRepository world, Entity e, TkbTemplate? template, KitPart[] parts, Vector3 size, KitPivots pivots, ArticulationPose pose,
                              MotionPose motion, Matrix4x4 kitToBody, Matrix4x4 body, Color colour, LitShader shader, double simTime)
    {
        if (template == null) return;
        var openings = VehicleOpeningsCatalog.Of(template);
        var lights = VehicleLightsCatalog.Of(template);
        if (openings.Length == 0 && lights.Length == 0) return;
        var boxes = BoxesOf(parts, size);
        var placed = kitToBody * body;
        var turretPlaced = placed;
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].Role == PartRole.Turret)
            {
                if (Matrix4x4.Invert(parts[i].BodyTransform(size, pivots, ArticulationPose.Neutral, motion), out var inv))
                    turretPlaced = inv * parts[i].BodyTransform(size, pivots, pose, motion) * placed;
                break;
            }
        foreach (var o in openings)
        {
            if (!VehicleSockets.Opening(o.Socket, o.Kind, boxes, out var panel)) continue;
            float progress = VehicleLook.OpeningProgress(world, e, o.Id, simTime);
            var frame = panel.OnTurret ? turretPlaced : placed;
            var (centre, basis, half) = panel.BoxAt(progress);
            DrawPart(_cube, Matrix4x4.CreateScale(half * 2f) * basis * Matrix4x4.CreateTranslation(centre), colour, frame, shader);
            OpeningsDrawn++;
            if (o.Kind == OpeningKind.Hatch && progress >= 0.9f && VehicleLook.CrewExposed(world, e, o.Id))
            {
                var crew = VehicleSockets.ExposedCrew(panel);
                DrawPart(_cube, Matrix4x4.CreateScale(crew.ShouldersHalf * 2f) * Matrix4x4.CreateTranslation(crew.Shoulders), CrewUniform, frame, shader);
                DrawPart(_cube, Matrix4x4.CreateScale(crew.HeadRadius * 1.6f) * Matrix4x4.CreateTranslation(crew.Head), CrewSkin, frame, shader);
                DrawPart(_cube, Matrix4x4.CreateScale(new Vector3(0.24f, 0.24f, 0.08f)) * Matrix4x4.CreateTranslation(crew.Head + new Vector3(0f, 0f, crew.HeadRadius)), CrewUniform, frame, shader);
                CrewOutDrawn++;
            }
        }
        if (lights.Length == 0) return;
        uint mask = VehicleLook.Lights(world, e, simTime, _brakes);
        if (mask == 0u) return;
        Span<LightPoint> points = stackalloc LightPoint[2];
        for (int i = 0; i < lights.Length && i < 32; i++)
        {
            if ((mask & (1u << i)) == 0) continue;
            int n = VehicleSockets.Lights(lights[i].Socket, lights[i].Kind, boxes, points);
            for (int k = 0; k < n; k++)
            {
                var c = points[k].Colour;
                _lightsNow.Add((Vector3.Transform(points[k].At, points[k].OnTurret ? turretPlaced : placed), new Color(c.R, c.G, c.B, c.A)));
            }
        }
    }

    /// <summary>⭐ CE-4670 (§7d D-5) — a closed-down commander's eye: at the periscope, this far above the turret's roof (m); head out it is
    /// <see cref="HatchEyeAboveRoof"/>.</summary>
    public const float PeriscopeAboveRoof = 0.12f;

    /// <summary>
    /// ⭐ CE-4670 (§7d D-5) — the commander's eye (CE-1200's, above the turret) follows his hatch: head out ⇒ as before; the hatch closed (the vehicle carries the
    /// opening state, its type has a <c>hatch_commander</c>) ⇒ at the periscope. A kind whose eye is in a cab (glass) or a type with no such hatch is unchanged.
    /// </summary>
    internal static Vector3 CommanderEye(EntityRepository world, Entity vehicle, VisualFamily family, Vector3 eye, ITkbDatabase? tkb, long type)
    {
        foreach (var p in ShapeKits.For(family).Parts) if (p.Colour == ColourRole.Glass) return eye;   // a cab's eye: no hatch
        if (tkb == null || !tkb.TryGetByType(type, out var t) || !world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.VehicleOpenings>()
            || !world.HasComponent<Fdp.Toolkit.Behavior.Components.VehicleOpenings>(vehicle)) return eye;
        foreach (var o in VehicleOpeningsCatalog.Of(t))
            if (o.Socket == "hatch_commander")
                return VehicleLook.CrewExposed(world, vehicle, o.Id) ? eye : eye - new Vector3(0f, 0f, HatchEyeAboveRoof - PeriscopeAboveRoof);
        return eye;
    }

    /// <summary>The lights collected this frame: an unlit core and a faint halo each (the halo writes no depth), after the lit batch.</summary>
    private void DrawLightPoints(in RenderContext ctx)
    {
        LightsDrawn = _lightsNow.Count;
        if (_lightsNow.Count == 0) return;
        foreach (var (at, c) in _lightsNow) Raylib.DrawSphereEx(HrotToRaylib.Position(at), VehicleSockets.LightRadius, 6, 8, ctx.Emissive(c));   // ⭐ CE-1984 — seen by a sensor, lighting nothing
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        foreach (var (at, c) in _lightsNow) Raylib.DrawSphereEx(HrotToRaylib.Position(at), VehicleSockets.HaloRadius, 6, 8, ctx.Emissive(new Color(c.R, c.G, c.B, (byte)90)));
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
        _lightsNow.Clear();
    }
}
