using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.AddEntity;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐ CE-5010 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-8; mockup <c>docs/UX/terrain-editor.svg</c> panel E) — the ONE <b>Terrain</b> tab of Details for every NON-ground <see cref="TerrainFeature"/> (a ground shape has its own Ground tab). Rows by kind, label-first
/// (<see cref="FormGrid"/>, <c>SeparatorText</c> sections): a wall / fence / hedge — Kind (within the line family), Material, Height (the kind's default shown when 0); a wood / scrub — Kind (within the area family), Density, Seed + dice; a tree Row — Spacing; a Group — Count, Seed + dice;
/// a Scatter — Recipe, Density, Seed + dice; a lone tree / clutter item / house — Heading. Then <i>Contents</i>: the length or area and one sentence of what the feature does. ⛔ A kind changes only within its own shape (§8 R-16). Edits are one undo step each
/// (<see cref="TerrainFeatureOps"/>). ⚠ Deviation from the mockup, said: the <i>Add gate here</i> hint is not drawn — the gate is TE-7 (CE-5012), not built.
/// </summary>
public sealed class TerrainFeatureDetailsView : IDetailsViewInstance
{
    private const string Form = "terrain.feature";

    private readonly Func<EntityRepository?> _world;
    private readonly Func<IReadOnlyList<(string Id, string Name, float Density)>> _recipes;
    private Entity _entity = Entity.Null;
    private float _pad; private string _seed = ""; private float _heading;
    private static readonly string[] KindIds = { "Wall##k_wall", "Fence##k_fence", "Hedge##k_hedge", "Wood##k_wood", "Scrub##k_scrub" };

    /// <summary>The screen rectangles of what the last <see cref="Draw"/> placed: <c>kind:&lt;name&gt;</c>, <c>material</c>, <c>height</c>, <c>density</c>, <c>spacing</c>, <c>count</c>, <c>seed</c>, <c>dice</c>, <c>recipe</c>, <c>heading</c>, <c>contents</c> — the frame rail's measure.</summary>
    public Dictionary<string, (Vector2 Min, Vector2 Max)> Rects { get; } = new();
    /// <summary>The Contents line the last frame showed.</summary>
    public string ContentsText { get; private set; } = "";
    /// <summary>The gate line of a closed wall the last frame showed, or "".</summary>
    public string GateText { get; private set; } = "";
    /// <summary>The hint beside the Height field the last frame showed ("the kind's default: 2.0 m"), or "".</summary>
    public string HeightHint { get; private set; } = "";

    public TerrainFeatureDetailsView(Func<EntityRepository?> world, Func<IReadOnlyList<(string Id, string Name, float Density)>> recipes)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
    }

    /// <summary>The feature's drawn points (terrain-local): its position + its <see cref="EditablePolyline"/> (relative); none for a single item.</summary>
    public static Vector2[] PointsOf(EntityRepository world, Entity e) => GroundFeatureDetailsView.PointsOf(world, e);

    /// <summary>The Contents line of a feature: its length or area and one sentence of what it does (walls: stops every vehicle, a heavy tank breaks it, blasts breach it; a wood blocks sight; a row / group: N lone trees).</summary>
    public static string ContentsOf(EntityRepository world, Entity e)
    {
        var f = world.GetComponentRO<TerrainFeature>(e);
        var pts = PointsOf(world, e);
        string len = $"{TerrainDrawOptions.Length(pts):0} m", area = TerrainDrawPanel.Hectares(TerrainDrawOptions.Area(pts));
        return f.Kind switch
        {
            TerrainFeature.Wall or TerrainFeature.Fence or TerrainFeature.Hedge => $"{len} · stops every vehicle; a heavy tank breaks it (B-3); blasts breach it (B-2)",   // (+ GateLineOf: a closed one says how to add a gate)
            TerrainFeature.Wood => $"{area} · blocks sight inside",
            TerrainFeature.Scrub => $"{area} · conceals those inside",
            TerrainFeature.Row => $"{FeatureExpander.CountAlong(pts, f.Pad > 0f ? f.Pad : FeatureExpander.DefaultRowSpacing)} lone trees — they fell, burn and block like any lone tree",
            TerrainFeature.Group => $"{(f.Pad > 0f ? (int)f.Pad : FeatureExpander.DefaultGroupCount)} lone trees — they fell, burn and block like any lone tree",
            TerrainFeature.Scatter => $"{area} · baked scenery, inert — Unpack makes single items",
            TerrainFeature.Tree => "one lone tree — it fells, burns and blocks like any lone tree",
            TerrainFeature.Clutter => "one baked clutter item — scenery, inert",
            TerrainFeature.Water => $"{area} · impassable",
            TerrainFeature.Bridge => $"{len} · a deck with ramps, joined to both banks",
            TerrainFeature.House => "one house",
            TerrainFeature.Lane => $"{area} · open ground",
            _ => "",
        };
    }

    /// <summary>⭐ CE-5009 (TE-7) — the second Contents line of a CLOSED wall / fence / hedge ("a closed wall — Edit Shape, right-click a side ▸ Add gate here (3 m)"); empty for an open line and every other kind.</summary>
    public static string GateLineOf(EntityRepository world, Entity e)
    {
        var f = world.GetComponentRO<TerrainFeature>(e);
        if (f.Kind is not (TerrainFeature.Wall or TerrainFeature.Fence or TerrainFeature.Hedge)) return "";
        var pts = PointsOf(world, e);
        return WallGate.IsClosed(pts) ? "a closed wall — Edit Shape, right-click a side ▸ Add gate here (" + WallGate.Width.ToString("0", CultureInfo.InvariantCulture) + " m)" : "";
    }

    /// <summary>The "the kind's default: 2.0 m" hint of a Height field showing the default (empty when a height was set).</summary>
    public static string HeightHintOf(in TerrainFeature f)
        => f.Pad > 0f ? "" : $"the kind's default: {TerrainFeatureOps.DefaultHeight(f.Kind).ToString("0.0", CultureInfo.InvariantCulture)} m";

    public void Draw(DetailsContext context, string idScope)
    {
        if (context.Entities is not { Count: 1 } || _world() is not { } world) return;
        var e = context.Entities[0];
        if (!TerrainFeatureOps.IsEditable(world, e)) { ImGui.TextDisabled("Select one terrain feature."); return; }
        var f = world.GetComponentRO<TerrainFeature>(e);
        if (e != _entity || !ImGui.IsAnyItemActive()) { _pad = TerrainFeatureOps.PadShown(f); _heading = f.HeadingDeg; }
        if (e != _entity || !ImGui.IsAnyItemActive()) _seed = f.Seed.ToString();
        _entity = e;
        Rects.Clear(); HeightHint = "";
        ImGui.PushID(idScope); ImGui.PushID("terrain");
        float w = ImGui.GetFontSize() / 14f;

        var family = TerrainFeatureOps.FamilyOf(f.Kind);
        if (family is not null)   // Kind — only within the SAME shape (a line stays a line, an area an area)
        {
            FormGrid.BeginField(Form, "Kind");
            for (int i = 0; i < family.Length; i++)
            {
                if (i > 0) ImGui.SameLine();
                byte k = family[i]; bool sel = k == f.Kind;
                if (sel) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
                if (ImGui.Button(TerrainFeatureOps.KindName(k) + "##k_" + k) && !sel) TerrainFeatureOps.SetKind(world, e, k);
                if (sel) ImGui.PopStyleColor();
                Rects["kind:" + TerrainFeatureOps.KindName(k)] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            }
            FormGrid.EndField();
        }

        if (family == TerrainFeatureOps.WallKinds)
        {
            FormGrid.BeginField(Form, "Material");
            ImGui.SetNextItemWidth(160f * w);
            string cur = f.Template.ToString() ?? "";
            string shown = cur.Length > 0 ? cur : TerrainFeatureOps.DefaultMaterial(f.Kind);
            if (ImGui.BeginCombo("##material", shown))
            {
                foreach (var m in TerrainMaterialLibrary.Shared.All.OrderBy(m => m.Name, StringComparer.Ordinal))
                    if (ImGui.Selectable(m.Name + "##mat_" + m.Name, m.Name == shown)) TerrainFeatureOps.SetMaterial(world, e, m.Name == TerrainFeatureOps.DefaultMaterial(f.Kind) ? "" : m.Name);
                ImGui.EndCombo();
            }
            Rects["material"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            FormGrid.EndField();

            FormGrid.BeginField(Form, "Height");
            ImGui.SetNextItemWidth(70f * w);
            ImGui.InputFloat("##height", ref _pad, 0f, 0f, "%.1f m");
            if (ImGui.IsItemDeactivatedAfterEdit()) TerrainFeatureOps.SetPad(world, e, _pad == TerrainFeatureOps.DefaultHeight(f.Kind) && f.Pad == 0f ? 0f : _pad);   // typing the default back keeps "the kind's default"
            Rects["height"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            HeightHint = HeightHintOf(f);
            if (HeightHint.Length > 0) { ImGui.SameLine(); ImGui.TextDisabled(HeightHint); }
            FormGrid.EndField();
        }
        else if (f.Kind is TerrainFeature.Wood or TerrainFeature.Scrub or TerrainFeature.Scatter)
        {
            if (f.Kind == TerrainFeature.Scatter) Recipe(world, e, f);
            if (f.Kind != TerrainFeature.Scrub) PadRow(world, e, f.Kind == TerrainFeature.Scatter ? "Density" : "Trees per ha", "density", "%.1f", f.Kind == TerrainFeature.Scatter ? "items per 100 m² · 0 = the recipe's" : "0 = the default");
        }
        else if (f.Kind == TerrainFeature.Row) PadRow(world, e, "Spacing", "spacing", "%.1f m", "");
        else if (f.Kind == TerrainFeature.Group) PadRow(world, e, "Count", "count", "%.0f", "");

        if (TerrainFeatureOps.HasSeed(f.Kind)) Seed(world, e, f);

        if (TerrainFeatureOps.HasHeading(f.Kind))
        {
            FormGrid.BeginField(Form, "Heading");
            ImGui.SetNextItemWidth(70f * w);
            ImGui.InputFloat("##heading", ref _heading, 0f, 0f, "%.0f°");
            if (ImGui.IsItemDeactivatedAfterEdit()) TerrainFeatureOps.SetHeading(world, e, _heading);
            Rects["heading"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            FormGrid.EndField();
        }

        ImGui.SeparatorText("Contents");
        ContentsText = ContentsOf(world, e);
        ImGui.TextUnformatted(ContentsText);
        Rects["contents"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        GateText = GateLineOf(world, e);
        if (GateText.Length > 0) { ImGui.TextDisabled(GateText); Rects["gate"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax()); }
        ImGui.PopID(); ImGui.PopID();
    }

    private void PadRow(EntityRepository world, Entity e, string label, string key, string format, string note)
    {
        FormGrid.BeginField(Form, label);
        ImGui.SetNextItemWidth(70f * ImGui.GetFontSize() / 14f);
        ImGui.InputFloat("##" + key, ref _pad, 0f, 0f, format);
        if (ImGui.IsItemDeactivatedAfterEdit()) TerrainFeatureOps.SetPad(world, e, _pad);
        Rects[key] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (note.Length > 0) { ImGui.SameLine(); ImGui.TextDisabled(note); }
        FormGrid.EndField();
    }

    private void Recipe(EntityRepository world, Entity e, in TerrainFeature f)
    {
        FormGrid.BeginField(Form, "Recipe");
        ImGui.SetNextItemWidth(-1f);
        string cur = f.Template.ToString() ?? "";
        var recipes = _recipes();
        string shown = recipes.FirstOrDefault(r => r.Id == cur).Name ?? TerrainPickCatalog.Title(cur);
        if (ImGui.BeginCombo("##recipe", shown))
        {
            foreach (var r in recipes) if (ImGui.Selectable(r.Name + "##r_" + r.Id, r.Id == cur)) TerrainFeatureOps.SetRecipe(world, e, r.Id);
            ImGui.EndCombo();
        }
        Rects["recipe"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        FormGrid.EndField();
    }

    private void Seed(EntityRepository world, Entity e, in TerrainFeature f)
    {
        FormGrid.BeginField(Form, "Seed");
        var dice = Icons.Get("terrain/reroll");
        ImGui.SetNextItemWidth(-(GlyphWidgets.WidthOf(dice) + ImGui.GetStyle().ItemSpacing.X));
        ImGui.InputText("##seed", ref _seed, 10, ImGuiInputTextFlags.CharsDecimal);
        if (ImGui.IsItemDeactivatedAfterEdit() && uint.TryParse(_seed, out uint typed)) TerrainFeatureOps.SetSeed(world, e, typed);
        Rects["seed"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine();
        if (GlyphWidgets.GlyphButton("##reroll", dice, "Re-roll: a new seed, same place")) TerrainFeatureOps.Reroll(world, e);
        Rects["dice"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        FormGrid.EndField();
    }

    public void Dispose() { }
}

/// <summary>The Terrain Details view's descriptor — offered for exactly one selected NON-ground <see cref="TerrainFeature"/>, while authoring.</summary>
public static class TerrainFeatureDetailsViewDescriptor
{
    public const string ViewId = "details.terrainfeature";
    /// <summary>Beside the Block (37) and Ground (38) tabs.</summary>
    public const int Rank = 39;

    public static DetailsViewDescriptor For(Func<EntityRepository?> world, Func<IReadOnlyList<(string Id, string Name, float Density)>> recipes)
    {
        ArgumentNullException.ThrowIfNull(world); ArgumentNullException.ThrowIfNull(recipes);
        var instance = new TerrainFeatureDetailsView(world, recipes);
        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Terrain",
            Rank:      Rank,
            AppliesTo: ctx => DetailsViewPredicates.ExactlyOneEntity(ctx) && world() is { } w && TerrainFeatureOps.IsEditable(w, ctx.Entities[0]),
            Create:    () => instance)
        {
            Requires     = new[] { typeof(TerrainFeature) },
            Availability = new Hrot.Common.Actions.ActionAvailability(Hrot.Common.Actions.ActionModes.InEdit),
        };
    }
}
