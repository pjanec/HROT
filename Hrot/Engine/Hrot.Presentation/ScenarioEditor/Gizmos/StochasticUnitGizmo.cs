using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common.Components;
using Hrot.Presentation.Map2D;
using FixedString32 = Fdp.Core.FixedString32;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-8603</c> (exp/agg-ui, ST-1 / ST-3; docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §4 UI-2, UI-4, UI-5, UI-6) — a STOCHASTIC unit on the 2-D map:
/// it has no members, so before this nothing drew it but its card. Its FOOTPRINT (width × depth × heading — <see cref="AggregateUnit"/>, §14 M-3): a side-coloured
/// translucent fill with a DASHED edge (not physical) and a heading arrow; a march column with a known route (<see cref="UnitRoute"/>, on the owning node) is the
/// band of its path from the tail to the head. A small FRAME at its centre with the echelon marks; one label row coloured by its strength (<c>name · present /
/// authorized · ~</c>, ~ = not physical); a LOSS mark (<c>−n</c>, 4 s) when the present strength falls — derived, nothing new on the wire. ⛔ A physical or fusion
/// unit is unchanged (its men are the picture), and nothing here draws in 3-D (the unit is hidden). Layer 0 (Entities), as every unit's symbol. 0 B on a steady
/// frame: label texts are cached per unit and rebuilt only when the numbers change.
/// </summary>
[GizmoProjector(typeof(AggregateUnit), typeof(SimTransform), DrawsOffScreen = true)]
public sealed class StochasticUnitGizmo : IStatelessGizmo
{
    /// <summary>The fill's alpha (≈ 22 %, the mockup).</summary>
    public const byte FillAlpha = 56;
    /// <summary>How long a loss mark stays (sim seconds).</summary>
    public const double LossMarkSeconds = 4.0;
    /// <summary>The frame's half size (screen pixels).</summary>
    public static readonly Vector2 FrameHalfPx = new(18f, 12f);

    public static readonly Rgba32 Friend = new(0, 100, 255, 230), Hostile = new(255, 0, 0, 230), Neutral = new(0, 200, 0, 230), Unknown = new(230, 230, 230, 230);
    public static readonly Rgba32 FriendFrame = new(128, 224, 255, 255), HostileFrame = new(255, 128, 128, 255), NeutralFrame = new(170, 255, 170, 255), UnknownFrame = new(255, 255, 128, 255);
    public static readonly Rgba32 Green = new(91, 208, 106, 255), Amber = new(242, 192, 51, 255), Red = new(230, 85, 85, 255), Black = new(20, 20, 20, 255), Ink = new(0, 0, 0, 255);

    private sealed class State
    {
        public int Present = -1, Authorized = -1;
        public FixedString32 Label;
        public FixedString32 Echelon;
        public byte EchelonOf = 255;
        public int LastLoss;
        public double LossAt = double.NegativeInfinity;
        public FixedString32 LossText;
    }

    private readonly Dictionary<ulong, State> _states = new();

    public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
    {
        ref readonly var u = ref view.GetComponentRO<AggregateUnit>(entity);
        if (u.Effective != AggregateResolution.Stochastic) return;   // a physical / fusion unit: its men are the picture (UI-2)
        var at = view.GetComponentRO<SimTransform>(entity).Position;
        var centre = new Vector2(at.X, at.Y);
        var (edge, frame) = SideColours(view, entity);
        var fill = new Rgba32(edge.R, edge.G, edge.B, FillAlpha);

        // ── the footprint: a column along its route, else the deployed rectangle ──
        if (!(u.Formation == UnitFormation.Column && TryColumn(view, entity, in u, edge, draw)))
        {
            Span<Vector2> c = stackalloc Vector2[4];
            UnitFootprint.Corners(centre, MathF.Max(1f, u.Width), MathF.Max(1f, u.Depth), u.Heading.Degrees, c);
            draw.DrawFilledTriangle(c[0], c[1], c[2], fill);
            draw.DrawFilledTriangle(c[0], c[2], c[3], fill);
            for (int i = 0; i < 4; i++)
                draw.DrawLine(new Vector3(c[i], 0f), new Vector3(c[(i + 1) & 3], 0f), edge, 2f, style: LineStyle.Dashed);
            var (b, tip) = UnitFootprint.FrontArrow(centre, MathF.Max(1f, u.Depth), u.Heading.Degrees, MathF.Max(8f, u.Depth * 0.3f));
            draw.DrawArrow(new Vector3(b, 0f), new Vector3(tip, 0f), edge, MathF.Max(6f, u.Depth * 0.12f));
        }

        // ── the frame and its echelon marks ──
        var st = StateOf(entity);
        draw.DrawBox2D(centre, FrameHalfPx, Ink, sizeMode: SizeMode.ScreenPixels, thickness: 1.5f, fillColor: frame);
        if (st.EchelonOf != u.Echelon) { st.EchelonOf = u.Echelon; st.Echelon = new FixedString32(EchelonMarks(u.Echelon)); }
        draw.DrawText(centre.X, centre.Y, st.Echelon, Ink, fontSizePx: 14f, lineOffsetPx: -30f);   // above the frame (text starts at the point: the 2-D text has no x offset)

        // ── the label row, coloured by strength; the loss mark ──
        if (view.HasComponent<UnitSlotTable>(entity))
        {
            ref readonly var slots = ref view.GetComponentRO<UnitSlotTable>(entity);
            int present = slots.Present(), authorized = slots.Authorized();
            double now = Now(view);
            if (st.Present >= 0 && present < st.Present) { st.LastLoss = st.Present - present; st.LossAt = now; st.LossText = new FixedString32("-" + st.LastLoss.ToString(CultureInfo.InvariantCulture)); }
            if (present != st.Present || authorized != st.Authorized)
            {
                st.Present = present; st.Authorized = authorized;
                st.Label = new FixedString32(Truncate($"{NameOf(view, entity)} · {present}/{authorized} · ~"));
            }
            float f = authorized > 0 ? (float)present / authorized : 0f;
            var colour = present <= 0 ? Black : f >= 0.9f ? Green : f >= 0.7f ? Amber : Red;   // the SITREP thresholds (StaffComposerSystem.GreenFraction / AmberFraction)
            draw.DrawText(centre.X, centre.Y, st.Label, colour, fontSizePx: 12f, lineOffsetPx: 14f, style: GizmoTextStyle.Backdrop);   // under the frame, on a backdrop (reads over any ground)
            if (now - st.LossAt <= LossMarkSeconds)
            {
                var front = UnitFootprint.FrontArrow(centre, MathF.Max(1f, u.Depth), u.Heading.Degrees, 0f).Base;
                draw.DrawText(front.X, front.Y, st.LossText, Red, fontSizePx: 13f);
            }
        }
    }

    private Vector2[] _path = new Vector2[64], _seg = new Vector2[66];

    /// <summary>The march column (UI-4): the route's last <c>Depth</c> metres up to the head (<see cref="AggregateUnit.PathDistanceM"/>) as a translucent band
    /// with a dashed centre line. Only where the route exists (<see cref="UnitRoute"/> is NoReplay and node-local — elsewhere the rectangle stands in).</summary>
    private bool TryColumn(ISimulationView view, Entity entity, in AggregateUnit u, Rgba32 edge, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo || !repo.TryGetTable(typeof(UnitRoute), out _) || !repo.HasManagedComponent<UnitRoute>(entity)) return false;
        var route = view.GetManagedComponentRO<UnitRoute>(entity);
        if (route == null) return false;
        int n = route.Points.Length;
        if (n < 2) return false;
        if (_path.Length < n) { _path = new Vector2[n * 2]; _seg = new Vector2[(n * 2) + 2]; }   // grown on a longer route, never per frame
        for (int i = 0; i < n; i++) _path[i] = new Vector2(route.Points[i].X, route.Points[i].Y);
        int k = UnitFootprint.ColumnSegment(_path.AsSpan(0, n), u.PathDistanceM, MathF.Max(1f, u.Depth), _seg);
        if (k < 2) return false;
        var band = new Rgba32(edge.R, edge.G, edge.B, (byte)(FillAlpha * 2));
        float wide = MathF.Max(6f, MathF.Min(u.Width, 20f));
        for (int i = 0; i + 1 < k; i++)
        {
            var a = new Vector3(_seg[i], 0f); var b = new Vector3(_seg[i + 1], 0f);
            draw.DrawLine(a, b, band, wide, SizeMode.WorldMeters);
            draw.DrawLine(a, b, edge, 2f, style: LineStyle.Dashed);
        }
        return true;
    }

    private State StateOf(Entity e)
    {
        if (!_states.TryGetValue(e.PackedValue, out var s)) { s = new State(); _states[e.PackedValue] = s; }
        return s;
    }

    private static double Now(ISimulationView view)
        => view is EntityRepository repo && repo.HasSingletonUnmanaged<GlobalTime>() ? repo.GetSingletonUnmanagedRO<GlobalTime>().TotalTime : 0.0;

    /// <summary>The APP-6 echelon marks: · squad, ··· platoon, I company, II battalion (Fdp.Toolkit.Squad.Echelon: 0 squad, 1 platoon, 2 company).</summary>
    public static string EchelonMarks(byte echelon) => echelon switch { 0 => "·", 1 => "···", 2 => "I", 3 => "II", _ => "" };   // "·": the gizmo font has it (GizmoFont), "•" it has not

    private static (Rgba32 Edge, Rgba32 Frame) SideColours(ISimulationView view, Entity e)
    {
        if (!view.HasComponent<EntityInfo>(e)) return (Unknown, UnknownFrame);
        return view.GetComponentRO<EntityInfo>(e).ForceId switch
        {
            ForceId.Friend => (Friend, FriendFrame),
            ForceId.Hostile => (Hostile, HostileFrame),
            ForceId.Neutral => (Neutral, NeutralFrame),
            _ => (Unknown, UnknownFrame),
        };
    }

    private static string NameOf(ISimulationView view, Entity e)
        => view.HasComponent<EntityInfo>(e) && view.GetComponentRO<EntityInfo>(e).Name.ToString() is { Length: > 0 } n ? n : "unit";

    private static string Truncate(string s) => s.Length <= 31 ? s : s[..31];
}
