using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Hrot.UI.Common.AddEntity;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐ Terrain authoring U-3 (docs/UX/terrain-authoring-2-blocks.svg, the Details panel; docs/DESIGN_Terrain_Authoring.md §15a) — the <b>Block</b> tab of Details for a placed <see cref="TerrainBlock"/>: the Recipe (a dropdown of the shipped block
/// recipes, with the block's icon), the Seed (typed, and a dice that re-rolls — ONE undo step each, <see cref="TerrainBlockOps"/>), <i>Fit to the site</i> (the four <see cref="TerrainBlock.Fit"/> bits), and the Contents
/// (what the live bake made of it, <see cref="LiveTerrainBakeInfo"/>). ⭐ A Details view on the existing seam (<see cref="DetailsViewDescriptor"/>, registered beside Playable / Macro) — no parallel panel.
/// Recipe and Seed are label-first rows (<see cref="FormGrid"/>); the Fit checklist is the mockup's — a box, then its label.
/// </summary>
public sealed class TerrainBlockDetailsView : IDetailsViewInstance
{
    public const string PadsNote = "flattening comes later (S-5)";

    private readonly Func<EntityRepository?> _world;
    private readonly Func<IReadOnlyList<TerrainPick>> _recipes;
    private Entity _entity = Entity.Null;
    private int _seed;

    /// <summary>The screen rectangles of what the last <see cref="Draw"/> placed, by name — the frame rail's measure (<c>recipe</c>, <c>seed</c>, <c>dice</c>, <c>align</c>, <c>water</c>, <c>buildings</c>, <c>pads</c>).</summary>
    public Dictionary<string, (Vector2 Min, Vector2 Max)> Rects { get; } = new();
    /// <summary>The Contents line the last frame showed.</summary>
    public string ContentsText { get; private set; } = "";

    public TerrainBlockDetailsView(Func<EntityRepository?> world, Func<IReadOnlyList<TerrainPick>> recipes)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
    }

    /// <summary>"28 houses · 3 woods" from the live bake; "not baked yet" before the first bake holds the block. ⚠ Clutter items are not counted: the block expansion makes none yet (the overlay's summary row says <c>clutter: 0</c>; backend's scatter, T-3).</summary>
    public static string ContentsOf(EntityRepository world, Entity block)
    {
        if (!world.HasSingletonManaged<LiveTerrainBakeInfo>() || !world.HasComponent<EntityGuid>(block)) return "not baked yet";
        var info = world.GetSingletonManaged<LiveTerrainBakeInfo>();
        if (info is null || !info.TryGet(world.GetComponentRO<EntityGuid>(block).Value, out var b)) return "not baked yet";
        return $"{b.Houses} houses · {b.Woods} woods";
    }

    public void Draw(DetailsContext context, string idScope)
    {
        if (context.Entities is not { Count: 1 } || _world() is not { } world) return;
        var e = context.Entities[0];
        if (!world.IsAlive(e) || !world.IsComponentTypeRegistered<TerrainBlock>() || !world.HasComponent<TerrainBlock>(e)) { ImGui.TextDisabled("Select one terrain block."); return; }
        if (e != _entity) { _entity = e; _seed = (int)world.GetComponentRO<TerrainBlock>(e).Seed; }
        var block = world.GetComponentRO<TerrainBlock>(e);
        Rects.Clear();
        ImGui.PushID(idScope + "/block");

        // ONE form id for every row (BeginField): the label column is learned from the widest of them all — "Align to the nearest road" included — so the sections' rows line up
        {
            // Recipe — the block icon, then the dropdown of the shipped recipes
            FormGrid.BeginField(Form, "Recipe");
            var icon = Icons.Get("terrain/block");
            ImGui.TextColored(icon.Color, icon.Glyph); ImGui.SameLine();
            ImGui.SetNextItemWidth(-1f);
            string current = block.Recipe.ToString() ?? "";
            var recipes = _recipes();
            string shown = recipes.FirstOrDefault(p => p.Kind == TerrainPickKind.Block && p.Id == current)?.Name ?? TerrainPickCatalog.Title(current);
            if (ImGui.BeginCombo("##recipe", shown))
            {
                foreach (var pick in recipes)
                {
                    if (pick.Kind != TerrainPickKind.Block) continue;
                    if (ImGui.Selectable(pick.Name + "##r_" + pick.Id, pick.Id == current)) TerrainBlockOps.ChangeRecipe(world, e, pick.Id);
                }
                ImGui.EndCombo();
            }
            Rects["recipe"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            FormGrid.EndField();

            // Seed — typed (committed on Enter / leaving the field) and a dice that re-rolls
            FormGrid.BeginField(Form, "Seed");
            var dice = Icons.Get("terrain/reroll");
            ImGui.SetNextItemWidth(-(GlyphWidgets.WidthOf(dice) + ImGui.GetStyle().ItemSpacing.X));
            if (!ImGui.IsAnyItemActive()) _seed = (int)block.Seed;
            ImGui.InputInt("##seed", ref _seed, 0, 0);
            if (ImGui.IsItemDeactivatedAfterEdit()) TerrainBlockOps.SetSeed(world, e, (uint)Math.Max(0, _seed));
            Rects["seed"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.SameLine();
            if (GlyphWidgets.GlyphButton("##reroll", dice, "Re-roll the layout: a new seed, same place")) TerrainBlockOps.Reroll(world, e);
            Rects["dice"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            FormGrid.EndField();
        }

        ImGui.SeparatorText("Fit to the site");
        Fit(world, e, block.Fit, "Align to the nearest road", "align", TerrainBlock.AlignRoad, true, null);
        Fit(world, e, block.Fit, "Keep out of water", "water", TerrainBlock.AvoidWater, true, null);
        Fit(world, e, block.Fit, "Keep out of buildings", "buildings", TerrainBlock.AvoidBuildings, true, null);
        Fit(world, e, block.Fit, "Level houses on pads", "pads", TerrainBlock.LevelPads, enabled: false, PadsNote);
        ImGui.TextDisabled(PadsNote);

        ImGui.SeparatorText("Contents");
        ContentsText = ContentsOf(world, e);
        ImGui.TextUnformatted(ContentsText);
        ImGui.PopID();
    }

    /// <summary>One Fit checkbox as the mockup draws it — the box, then its label (the label is plain text, not the widget's: the label-first rail has no say over a checklist). A greyed one shows its bit and says why on hover.</summary>
    private void Fit(EntityRepository world, Entity e, byte fit, string label, string key, byte bit, bool enabled, string? reason)
    {
        bool on = (fit & bit) != 0;
        ImGui.BeginDisabled(!enabled);
        if (ImGui.Checkbox("##fit_" + key, ref on)) TerrainBlockOps.SetFit(world, e, bit, on);
        Rects[key] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        bool hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.EndDisabled();
        if (reason != null && (hovered || ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))) ImGui.SetTooltip(reason);
    }

    private const string Form = "terrain.block";

    public void Dispose() { }
}

/// <summary>The Block Details view's descriptor — offered for exactly one selected <see cref="TerrainBlock"/>, while authoring.</summary>
public static class TerrainBlockDetailsViewDescriptor
{
    public const string ViewId = "details.terrainblock";
    /// <summary>Above the generic Components tab, beside Playable (36).</summary>
    public const int Rank = 37;

    public static DetailsViewDescriptor For(Func<EntityRepository?> world, Func<IReadOnlyList<TerrainPick>> recipes)
    {
        ArgumentNullException.ThrowIfNull(world);
        var instance = new TerrainBlockDetailsView(world, recipes);
        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Block",
            Rank:      Rank,
            AppliesTo: ctx => DetailsViewPredicates.ExactlyOneEntity(ctx) && world() is { } w && w.IsAlive(ctx.Entities[0]),
            Create:    () => instance)
        {
            Requires     = new[] { typeof(TerrainBlock) },
            Availability = new Hrot.Common.Actions.ActionAvailability(Hrot.Common.Actions.ActionModes.InEdit),
        };
    }
}
