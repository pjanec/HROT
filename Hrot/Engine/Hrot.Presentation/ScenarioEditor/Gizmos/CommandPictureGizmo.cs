using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Squad.Command;
// Disambiguate from GizmoMap.Contracts' FixedString32 (the gizmo namespace re-exports it).
using FixedString32 = Fdp.Core.FixedString32;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-1192</c> (UI-5b, 📄 docs/DESIGN_Command_Display.md §6 D2) — the SELECTED / pinned commander's <see cref="CommandPicture"/>: each
/// reported element a diamond at where it was seen, its side by its size class, a letter per equipment flag (M machine gun · S sniper ·
/// A anti-tank · V vehicle), its count and age, fading with age (<see cref="Alpha"/>); a thin line to each subordinate it heard from,
/// labelled with that unit's last PROGRESS; a <c>?</c> and the kind at each open request. Family <c>SquadAssignment</c>, as
/// <c>SquadGizmo</c> beside it. ⚠ The picture is brain-local (D4): this draws on a node that holds it (SimHost's map today).
/// </summary>
/// <summary>
/// ⭐ <c>CE-1192</c> D4 (behaviors' answer, R-2058: the picture is replicated read-only to every node) — a SELECTED commander whose
/// <see cref="CommandPicture"/> has not reached this node yet says so over his head, rather than drawing nothing: 🔒 the user asked for
/// absence to be visible. A commander = a unit some <see cref="UnitSubordinate"/> names as its commander.
/// </summary>
[GizmoProjector(typeof(SimTransform), Family = AiOverlayFlags.SquadAssignment)]
public sealed class CommandPictureAbsentGizmo : IStatelessGizmo
{
    public const string Text = "no picture on this node yet";   // ≤ 31 bytes (FixedString32)
    public static readonly Rgba32 Colour = new(200, 200, 200, 220);

    public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
    {
        if (!draw.IsLayerActive(DebugTraceLayers.SquadCommand)) return;   // ⭐ CE-1203 — its own checkbox (§5e FL-3): a hidden layer costs no work
        if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<UnitSubordinate>()) return;
        if (repo.IsComponentTypeRegistered<CommandPicture>() && repo.HasComponent<CommandPicture>(entity)) return;
        if (!IsCommander(repo, entity)) return;
        var at = view.GetComponentRO<SimTransform>(entity).Position;
        draw.DrawText(at.X, at.Y, new FixedString32(Text), Colour, fontSizePx: 10f, lineOffsetPx: -28f, layer: DebugTraceLayers.SquadCommand);
    }

    /// <summary>True when some unit names <paramref name="unit"/> as its commander.</summary>
    public static bool IsCommander(EntityRepository world, Entity unit)
    {
        foreach (var e in world.Query().With<UnitSubordinate>().Build())
            if (world.GetComponentRO<UnitSubordinate>(e).Commander == unit) return true;
        return false;
    }
}

[GizmoProjector(typeof(CommandPicture), typeof(SimTransform), Family = AiOverlayFlags.SquadAssignment)]
public sealed class CommandPictureGizmo : IStatelessGizmo
{
    public static readonly Rgba32 ElementColour = new(230, 60, 40, 255);
    public static readonly Rgba32 SubordinateColour = new(120, 120, 120, 200);
    public static readonly Rgba32 RequestColour = new(240, 200, 0, 255);
    /// <summary>An element fades to <see cref="MinAlpha"/> over this many seconds since it was observed.</summary>
    public const float FadeSeconds = 120f;
    public const float MinAlpha = 0.25f;

    public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
    {
        if (!draw.IsLayerActive(DebugTraceLayers.SquadCommand)) return;   // ⭐ CE-1203 — its own checkbox (§5e FL-3): a hidden layer costs no work
        var at = view.GetComponentRO<SimTransform>(entity).Position;
        ref readonly var pic = ref view.GetComponentRO<CommandPicture>(entity);
        float now = CommandPictureOps.Now(view);

        for (int i = 0; i < pic.Elements.Count && i < CommandPicture.MaxElements; i++)
        {
            ref readonly var e = ref pic.Elements.Items[i];
            float age = MathF.Max(0f, now - e.ObservedAt);
            var colour = WithAlpha(ElementColour, Alpha(age));
            Diamond(draw, e.At, HalfSize(e.Size), colour);
            draw.DrawText(e.At.X, e.At.Y, new FixedString32($"{e.Count}{Flags(e.Equipment)} {age:0}s{(e.Synthetic != 0 ? " SYN" : "")}"), colour,
                fontSizePx: 10f, lineOffsetPx: 14f, layer: DebugTraceLayers.SquadCommand);
        }

        if (view is EntityRepository repo)
            for (int i = 0; i < pic.Subordinates.Count && i < CommandPicture.MaxSubordinates; i++)
            {
                ref readonly var row = ref pic.Subordinates.Items[i];
                var sub = NetworkIdResolver.ResolveNetworkId(repo, row.UnitNetId);
                if (sub.IsNull || !repo.IsAlive(sub) || !repo.HasComponent<SimTransform>(sub)) continue;
                var s = repo.GetComponentRO<SimTransform>(sub).Position;
                draw.DrawLine(at, s, SubordinateColour, 1f, style: LineStyle.Dashed, layer: DebugTraceLayers.SquadCommand);
                draw.DrawText(s.X, s.Y, new FixedString32(row.Progress.ToString()), SubordinateColour, fontSizePx: 10f, lineOffsetPx: -14f, layer: DebugTraceLayers.SquadCommand);
            }

        for (int i = 0; i < pic.Requests.Count && i < CommandPicture.MaxRequests; i++)
        {
            ref readonly var r = ref pic.Requests.Items[i];
            draw.DrawText(r.At.X, r.At.Y, new FixedString32($"? {r.Kind}"), RequestColour, fontSizePx: 12f, layer: DebugTraceLayers.SquadCommand);
        }
    }

    /// <summary>1 at age 0, falling linearly to <see cref="MinAlpha"/> at <see cref="FadeSeconds"/> and staying there.</summary>
    public static float Alpha(float ageSeconds) => MathF.Max(MinAlpha, 1f - MathF.Max(0f, ageSeconds) / FadeSeconds);

    /// <summary>The diamond's half-diagonal (m) by size class.</summary>
    public static float HalfSize(ElementSize size) => size switch
    {
        ElementSize.Individual => 1f,
        ElementSize.Team => 2f,
        ElementSize.SquadSize => 3.5f,
        ElementSize.PlatoonSize => 6f,
        _ => 2f,
    };

    /// <summary>The equipment flags as letters: M machine gun · S sniper · A anti-tank · V vehicle.</summary>
    public static string Flags(ElementEquipment eq)
        => (eq.HasFlag(ElementEquipment.MachineGun) ? "M" : "") + (eq.HasFlag(ElementEquipment.Sniper) ? "S" : "")
         + (eq.HasFlag(ElementEquipment.AntiTank) ? "A" : "") + (eq.HasFlag(ElementEquipment.Vehicle) ? "V" : "");

    private static Rgba32 WithAlpha(Rgba32 c, float a) => new(c.R, c.G, c.B, (byte)Math.Clamp(a * 255f, 0f, 255f));

    private static void Diamond(IDebugDrawBuilder draw, Vector3 c, float h, Rgba32 colour)
    {
        var n = c + new Vector3(0, h, 0); var e = c + new Vector3(h, 0, 0); var s = c + new Vector3(0, -h, 0); var w = c + new Vector3(-h, 0, 0);
        draw.DrawLine(n, e, colour, 2f, layer: DebugTraceLayers.SquadCommand); draw.DrawLine(e, s, colour, 2f, layer: DebugTraceLayers.SquadCommand); draw.DrawLine(s, w, colour, 2f, layer: DebugTraceLayers.SquadCommand); draw.DrawLine(w, n, colour, 2f, layer: DebugTraceLayers.SquadCommand);
    }
}

/// <summary>
/// ⭐ <c>CE-1192</c> (UI-5b, 📄 docs/DESIGN_Command_Display.md §6 D2) — a selected / pinned man's own sector ARC (<see cref="EngagementArea"/>
/// <c>ArcSet</c>): two edges from the arc's origin along its compass bearings and the far edge at its range (50 m when unlimited), in his
/// unit's colour. Every man may carry one, so it is drawn only for the selection (family <c>SquadAssignment</c>). ⭐ <c>CE-1225</c> (§8 S-1,
/// S-3): on the <see cref="DebugTraceLayers.Sectors"/> layer, on the ground (z = 0, draped in 3-D); a COMMANDER's arc is also drawn for
/// everyone by <see cref="SectorAreasGizmo"/> (the same line twice when he is selected — S-5).
/// </summary>
[GizmoProjector(typeof(EngagementArea), typeof(SimTransform), Family = AiOverlayFlags.SquadAssignment)]
public sealed class SectorArcGizmo : IStatelessGizmo
{
    public const float UnlimitedDrawRange = 50f;

    public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
    {
        if (!draw.IsLayerActive(DebugTraceLayers.Sectors)) return;
        ref readonly var a = ref view.GetComponentRO<EngagementArea>(entity);
        if (a.ArcSet == 0) return;
        long net = view is EntityRepository repo && repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
            && view.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity)
            ? view.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity).Value : entity.Index;
        DrawArc(draw, in a, SectorAreasGizmo.ColourOf(net));
    }

    /// <summary>An arc's two edges and its far edge on the ground (z = 0) on the Sectors layer; nothing when it has no arc.</summary>
    public static void DrawArc(IDebugDrawBuilder draw, in EngagementArea a, Rgba32 colour)
    {
        if (a.ArcSet == 0) return;
        float far = a.MaxRange > 0f ? a.MaxRange : UnlimitedDrawRange;
        float near = MathF.Max(0f, a.MinRange);
        float left = a.LeftBearing, sweep = ((a.RightBearing - a.LeftBearing) % 360f + 360f) % 360f;
        if (sweep == 0f) sweep = 360f;
        var origin = new Vector3(a.ArcOrigin.X, a.ArcOrigin.Y, 0f);
        Line(draw, origin + Dir(left) * near, origin + Dir(left) * far, colour);
        Line(draw, origin + Dir(left + sweep) * near, origin + Dir(left + sweep) * far, colour);
        int steps = Math.Max(2, (int)(sweep / 10f));
        for (int i = 0; i < steps; i++)
        {
            float b0 = left + sweep * i / steps, b1 = left + sweep * (i + 1) / steps;
            Line(draw, origin + Dir(b0) * far, origin + Dir(b1) * far, colour);
        }
    }

    private static void Line(IDebugDrawBuilder draw, Vector3 from, Vector3 to, Rgba32 colour)
        => draw.DrawLine(from, to, colour, 1.5f, layer: DebugTraceLayers.Sectors);

    /// <summary>A compass bearing (0 = north +Y, 90 = east +X) as a unit vector on the map plane.</summary>
    public static Vector3 Dir(float bearingDeg)
    {
        float r = bearingDeg * MathF.PI / 180f;
        return new Vector3(MathF.Sin(r), MathF.Cos(r), 0f);
    }
}
