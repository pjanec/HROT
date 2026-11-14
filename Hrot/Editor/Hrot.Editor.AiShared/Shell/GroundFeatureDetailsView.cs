using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.AddEntity;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐ <c>CE-4592</c> / <c>CE-4593</c> (P-5 part 2b, P-6; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §9.16; mockup <c>docs/UX/ground-shaping-and-ditches.svg</c> C and D) — the <b>Ground</b> /
/// <b>Ditch</b> tab of Details for a placed ground shape: the Mode row (Level to Z · Raise by dZ · Lower by dZ · Crater greyed — V-14) or the Preset row (Ditch · Anti-tank ditch), the value
/// (label-first, <see cref="FormGrid"/>), the derived Skirt / Width / Bank slope, and the <b>Effect</b> section measured by the ONE <see cref="GroundEffect"/> (the grade, the area and change,
/// what is kept out, the draw order, the SUMO line; a ditch's cover, vehicles, infantry, the road stop). Edits are one undo step each (<see cref="GroundFeatureOps"/>).
/// ⭐ A Details view on the existing seam (<see cref="DetailsViewDescriptor"/>, beside the Block tab) — no parallel panel. The effect is rebuilt only when the shape changes (R-2084).
/// ⚠ Deviations from the mockup, said: Skirt, Width and Bank slope are READ-OUTS (derived — R-5026: no fields until the sibling component, V-27); the Seed row is not drawn (a shape's Seed is its draw order).
/// </summary>
public sealed class GroundFeatureDetailsView : IDetailsViewInstance
{
    private const string Form = "terrain.ground";
    /// <summary>The ground area's Mode row, as the mockup labels it.</summary>
    public static readonly (string Mode, string Name)[] Modes = { ("level", "Level to Z"), ("raise", "Raise by dZ"), ("lower", "Lower by dZ") };
    public static readonly (string Mode, string Name)[] Presets = { ("ditch", "Ditch"), ("at-ditch", "Anti-tank ditch"), ("foxhole", "Foxhole") };
    private static readonly string[] ButtonIds = { "Level to Z##m_level", "Raise by dZ##m_raise", "Lower by dZ##m_lower", "Ditch##m_ditch", "Anti-tank ditch##m_at-ditch", "Foxhole##m_foxhole" };
    private static readonly string[] RectIds = { "mode:Level to Z", "mode:Raise by dZ", "mode:Lower by dZ", "mode:Ditch", "mode:Anti-tank ditch", "mode:Foxhole" };
    private static readonly string[] EffectIds = { "effect:0", "effect:1", "effect:2", "effect:3", "effect:4", "effect:5", "effect:6" };
    public const string CraterNote = "a crater is a crater RECORD, not a ground shape — later (V-14)";

    private readonly Func<EntityRepository?> _world;
    private readonly Func<TerrainGhostContext?> _context;
    private Entity _entity = Entity.Null;
    private ulong _key; private TerrainGhostContext? _keyCtx;
    private GroundEffect _fx = GroundEffect.None;
    private readonly List<(string Glyph, string Text, string? Sub, bool Warn)> _effect = new();
    private float _value;
    private readonly List<(string Label, string Value, string Note)> _readOuts = new();

    /// <summary>The screen rectangles of what the last <see cref="Draw"/> placed (<c>mode:&lt;name&gt;</c>, <c>value</c>, <c>effect:&lt;i&gt;</c>) — the frame rail's measure.</summary>
    public Dictionary<string, (Vector2 Min, Vector2 Max)> Rects { get; } = new();
    /// <summary>The Effect rows as last drawn (glyph key, text) — what a rail compares with the mockup's words.</summary>
    public IReadOnlyList<(string Glyph, string Text, string? Sub, bool Warn)> Effect => _effect;

    public GroundFeatureDetailsView(Func<EntityRepository?> world, Func<TerrainGhostContext?> context)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>The shape's drawn points (terrain-local): its position + its <see cref="EditablePolyline"/> (relative); a foxhole has none.</summary>
    public static Vector2[] PointsOf(EntityRepository world, Entity e)
    {
        if (!world.HasComponent<SimTransform>(e)) return Array.Empty<Vector2>();
        var at = world.GetComponentRO<SimTransform>(e).Position;
        if (!world.HasManagedComponent<EditablePolyline>(e) || ((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetManagedComponentRO<EditablePolyline>(e) is not { } poly) return Array.Empty<Vector2>();
        var pts = new Vector2[poly.Points.Count];
        for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(at.X, at.Y) + poly.Points[i];
        return pts;
    }

    private Vector3 _ptsAt = new(float.NaN); private int _ptsVersion = -1; private Entity _ptsOf = Entity.Null; private Vector2[] _pts = Array.Empty<Vector2>();

    /// <summary>The points, rebuilt only when the shape moved or its outline changed (the polyline's <c>Version</c>) — not per frame (R-2084).</summary>
    private Vector2[] CachedPoints(EntityRepository world, Entity e)
    {
        var at = world.HasComponent<SimTransform>(e) ? world.GetComponentRO<SimTransform>(e).Position : default;
        int version = world.HasManagedComponent<EditablePolyline>(e) && ((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetManagedComponentRO<EditablePolyline>(e) is { } poly ? poly.Version : -2;
        if (e != _ptsOf || at != _ptsAt || version != _ptsVersion) { _ptsOf = e; _ptsAt = at; _ptsVersion = version; _pts = PointsOf(world, e); }
        return _pts;
    }

    public void Draw(DetailsContext context, string idScope)
    {
        if (context.Entities is not { Count: 1 } || _world() is not { } world) return;
        var e = context.Entities[0];
        if (!GroundFeatureOps.IsGround(world, e)) { ImGui.TextDisabled("Select one ground shape."); return; }
        var f = world.GetComponentRO<TerrainFeature>(e);
        string mode = f.Template.ToString() ?? "raise";
        bool ditch = DitchPresets.Of(mode) is not null;
        if (e != _entity || !ImGui.IsAnyItemActive()) _value = ditch && f.Pad == 0f ? DitchPresets.Of(mode)!.Value.Depth : f.Pad;
        _entity = e;
        Rects.Clear();
        ImGui.PushID(idScope); ImGui.PushID("ground");

        var ctx = _context();
        var pts = CachedPoints(world, e);
        Refresh(world, e, f, mode, pts, ctx);

        // the Mode / Preset row — one row of buttons, the current one selected
        FormGrid.BeginField(Form, ditch ? "Preset" : "Mode");
        var row = ditch ? Presets : Modes;
        bool foxhole = mode == "foxhole";
        for (int i = 0; i < row.Length; i++)
        {
            var (m, name) = row[i];
            if (i > 0) ImGui.SameLine();
            bool sel = m == mode;
            bool enabled = ditch ? (foxhole ? m == "foxhole" : m != "foxhole") : true;   // a foxhole is a click, a ditch a line: neither turns into the other
            ImGui.BeginDisabled(!enabled);
            if (sel) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
            if (ImGui.Button(ButtonIds[(ditch ? 3 : 0) + i]) && !sel) GroundFeatureOps.SetMode(world, e, m, DefaultValue(m, world, e, ctx));
            if (sel) ImGui.PopStyleColor();
            ImGui.EndDisabled();
            Rects[RectIds[(ditch ? 3 : 0) + i]] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        }
        if (!ditch)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(true); ImGui.Button("Crater##m_crater"); ImGui.EndDisabled();
            Rects["mode:Crater"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(CraterNote);
        }
        FormGrid.EndField();

        // the value
        string label = mode switch { "raise" => "Raise by", "lower" => "Lower by", "level" => "Level to", _ => "Depth" };
        FormGrid.BeginField(Form, label);
        ImGui.SetNextItemWidth(90f * ImGui.GetFontSize() / 14f);
        ImGui.InputFloat("##value", ref _value, 0f, 0f, "%.1f m");
        if (ImGui.IsItemDeactivatedAfterEdit()) GroundFeatureOps.SetValue(world, e, _value);
        Rects["value"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        FormGrid.EndField();

        // the derived read-outs (R-5026), built in Refresh
        for (int i = 0; i < _readOuts.Count; i++) ReadOut(_readOuts[i].Label, _readOuts[i].Value, _readOuts[i].Note);

        ImGui.SeparatorText("Effect");
        for (int i = 0; i < _effect.Count; i++)
        {
            var (g, t, sub, warn) = _effect[i];
            var icon = Icons.Get(g);
            ImGui.TextColored(icon.Color, icon.Glyph); ImGui.SameLine();
            if (warn) ImGui.TextColored(IconPalette.Warning, t); else ImGui.TextUnformatted(t);
            if (i < EffectIds.Length) Rects[EffectIds[i]] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            if (sub != null) { ImGui.Indent(ImGui.GetFontSize() * 1.4f); ImGui.TextDisabled(sub); ImGui.Unindent(ImGui.GetFontSize() * 1.4f); }
        }
        ImGui.PopID(); ImGui.PopID();
    }

    private static void ReadOut(string label, string value, string note)
    {
        FormGrid.BeginField(Form, label);
        ImGui.TextUnformatted(value); ImGui.SameLine(); ImGui.TextDisabled(note);
        FormGrid.EndField();
    }

    /// <summary>The value a mode switch sets: raise 2 m, lower 1 m, level the base ground at the shape's position, a ditch preset 0 (= its own depth).</summary>
    private static float DefaultValue(string mode, EntityRepository world, Entity e, TerrainGhostContext? ctx) => mode switch
    {
        "raise" => TerrainBirth.DefaultRaise,
        "lower" => TerrainBirth.DefaultLower,
        "level" => world.HasComponent<SimTransform>(e) && ctx?.Base.Height is { } g
            ? MathF.Round(g.Sample(world.GetComponentRO<SimTransform>(e).Position.X, world.GetComponentRO<SimTransform>(e).Position.Y) * 10f) / 10f : 0f,
        _ => 0f,
    };

    /// <summary>Rebuilds the effect and its rows when the shape (mode, value, points, position) or the base changed — a user action, never per frame.</summary>
    private void Refresh(EntityRepository world, Entity e, in TerrainFeature f, string mode, Vector2[] pts, TerrainGhostContext? ctx)
    {
        ulong key = GroundEffect.KeyOf(mode, f.Pad, pts) ^ ((ulong)(uint)e.Index << 32) ^ f.Seed;
        if (key == _key && ReferenceEquals(ctx, _keyCtx) && _effect.Count > 0) return;
        _key = key; _keyCtx = ctx;
        bool ditch = DitchPresets.Of(mode) is not null;
        try { _fx = ctx is null || pts.Length < 2 ? GroundEffect.None : GroundEffect.Of(ctx.Base, ctx.Roads, ctx.Assets, mode, f.Pad, pts); }
        catch (ArgumentException) { _fx = GroundEffect.None; }
        _effect.Clear(); _readOuts.Clear();
        if (ditch)
        {
            var p = DitchPresets.Of(mode)!.Value;
            float depth = f.Pad > 0f ? f.Pad : p.Depth, bank = 0.6f * depth + 0.3f;
            _readOuts.Add(("Width", mode == "foxhole" ? $"{p.Width:0.0} × {DitchPresets.FoxholeLength:0.0} m" : $"{p.Width:0.0} m", "the preset's"));
            _readOuts.Add(("Bank slope", $"{MathF.Atan2(depth, bank) * 180f / MathF.PI:0}°", "walls ≤ 60°: infantry climb"));
        }
        else _readOuts.Add(("Skirt", mode == "level" ? "≥ 4 m" : $"{MathF.Max(GroundPatches.MinSkirt, GroundPatches.SkirtPerMetreOfCut * MathF.Abs(f.Pad)):0.#} m", "the blend to natural ground"));
        var (older, newer, olderName) = Overlaps(world, e, f.Seed, pts);
        if (ditch) DitchRows(mode, f, pts, older, newer); else GroundRows(mode, f, older, newer, olderName);
    }

    private void GroundRows(string mode, in TerrainFeature f, int older, int newer, string? olderName)
    {
        var fx = _fx;
        if (ReferenceEquals(fx, GroundEffect.None)) _effect.Add(("form/info", "no terrain to measure on", null, false));
        else if (fx.RoadsKept == 0) _effect.Add(("form/info", "no road reached", null, false));
        else _effect.Add(("form/warning", fx.OverGrade ? $"steepest road grade {fx.RoadGrade * 100f:0} % — over 15 %" : $"steepest road grade {fx.RoadGrade * 100f:0} %", null, fx.OverGrade));
        string did = mode switch { "level" => $"levelled to {f.Pad:0.0} m", "lower" => $"lowered {f.Pad:0.0} m", _ => $"raised {f.Pad:0.0} m" };
        _effect.Add(("earth/mound", $"area {TerrainDrawPanel.Hectares(fx.Area)} · {did}", null, false));
        _effect.Add(("earth/kept", $"kept out: {Count(fx.HousesKept, "house")} + skirt · {Count(fx.RoadsKept, "road corridor")}", null, false));
        _effect.Add(("earth/layers", DrawOrderText(older, newer, olderName), null, false));
        _effect.Add(("terrain/roads", "SUMO net re-elevated at save (our roads follow now)", null, false));
    }

    private void DitchRows(string mode, in TerrainFeature f, Vector2[] pts, int older, int newer)
    {
        var p = DitchPresets.Of(mode)!.Value;
        float depth = f.Pad > 0f ? f.Pad : p.Depth;
        string stance = depth >= 1.5f ? "stand" : depth >= 0.9f ? "crouch" : depth >= 0.45f ? "prone" : "none";
        if (mode == "foxhole")
        {
            _effect.Add(("earth/cover", $"cover: 4 points all round · {stance}", "(the defence takes them — V-29)", false));
            _effect.Add(("earth/vehicle", "vehicles: not closed (a dip)", null, false));
        }
        else
        {
            _effect.Add(("earth/cover", $"cover: {stance} along both banks", "(floor points, a pair facing each bank — V-30)", false));
            _effect.Add(("earth/vehicle", "vehicles: wheeled closed · tracked closed", "(every vehicle avoids a ditch area — D-2)", false));
        }
        _effect.Add(("earth/infantry", "infantry passes (a small cost)", null, false));
        if (mode != "foxhole") _effect.Add(("terrain/roads", _fx.RoadsKept > 0 ? "stops at the road corridor (G-29)" : "crosses no road", null, false));
        float len = TerrainDrawOptions.Length(pts);
        _effect.Add(("earth/layers", mode == "foxhole" ? DrawOrderText(older, newer, null) : $"{len:0} m · {pts.Length} vertices · {(newer == 0 ? "newest on top" : $"under {Count(newer, "newer shape")}")}", null, false));
    }

    private static string DrawOrderText(int older, int newer, string? olderName)
    {
        if (newer > 0) return $"under {Count(newer, "newer shape")} — the newest wins (V-28)";
        if (older == 1 && olderName != null) return $"drawn over {olderName} — the newest wins (V-28)";
        if (older > 0) return $"drawn over {Count(older, "shape")} — the newest wins (V-28)";
        return "overlaps no other shape";
    }

    private static string Count(int n, string what) => n == 1 ? "1 " + what : $"{n} {what}s";

    /// <summary>The other ground shapes whose drawn box meets this one's: how many are older (a lower draw order) and newer, and the older one's name when there is one.</summary>
    private static (int Older, int Newer, string? OlderName) Overlaps(EntityRepository world, Entity self, uint order, Vector2[] pts)
    {
        if (pts.Length == 0) return (0, 0, null);
        var (lo, hi) = Box(pts);
        int older = 0, newer = 0; string? name = null;
        foreach (var other in world.Query().With<TerrainFeature>().Build())
        {
            if (other == self) continue;
            var o = world.GetComponentRO<TerrainFeature>(other);
            if (o.Kind != TerrainFeature.Ground) continue;
            var op = PointsOf(world, other);
            if (op.Length == 0) continue;
            var (a, b) = Box(op);
            if (b.X < lo.X || a.X > hi.X || b.Y < lo.Y || a.Y > hi.Y) continue;
            if (o.Seed < order) { older++; name = world.HasComponent<EntityInfo>(other) ? world.GetComponentRO<EntityInfo>(other).Name.ToString() : null; }
            else newer++;
        }
        return (older, newer, older == 1 ? name : null);
    }

    private static (Vector2 Min, Vector2 Max) Box(Vector2[] p)
    {
        var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
        foreach (var q in p) { min = Vector2.Min(min, q); max = Vector2.Max(max, q); }
        return (min, max);
    }

    public void Dispose() { }
}

/// <summary>The Ground / Ditch Details view's descriptor — offered for exactly one selected ground-shape <see cref="TerrainFeature"/>, while authoring.</summary>
public static class GroundFeatureDetailsViewDescriptor
{
    public const string ViewId = "details.groundfeature";
    /// <summary>Beside the Block tab (37).</summary>
    public const int Rank = 38;

    public static DetailsViewDescriptor For(Func<EntityRepository?> world, Func<TerrainGhostContext?> context)
    {
        ArgumentNullException.ThrowIfNull(world); ArgumentNullException.ThrowIfNull(context);
        var instance = new GroundFeatureDetailsView(world, context);
        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Ground",
            Rank:      Rank,
            AppliesTo: ctx => DetailsViewPredicates.ExactlyOneEntity(ctx) && world() is { } w && GroundFeatureOps.IsGround(w, ctx.Entities[0]),
            Create:    () => instance)
        {
            Requires     = new[] { typeof(TerrainFeature) },
            Availability = new Hrot.Common.Actions.ActionAvailability(Hrot.Common.Actions.ActionModes.InEdit),
        };
    }
}
