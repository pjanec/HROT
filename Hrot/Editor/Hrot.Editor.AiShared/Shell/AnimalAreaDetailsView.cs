using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Animals;
using Hrot.UI.Common.AddEntity;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐ CE-4623 (animals AU-7; docs/designs/animals/animals-visuals.svg panel F, DESIGN_Animals §6.3 / AN-16 / AN-18) — the <b>Animal area</b> tab of Details for a placed animal area: Species (the domestic ones), Count (1 … 200), Seed (typed, and a dice that re-rolls),
/// Enclosed (default on for pigs and goats). ⭐ A Details view on the existing seam (<see cref="DetailsViewDescriptor"/>, beside Block) — no parallel panel; every edit ONE undo step (<see cref="AnimalAreaOps"/>). Label-first rows (<see cref="FormGrid"/>).
/// </summary>
public sealed class AnimalAreaDetailsView : IDetailsViewInstance
{
    private readonly Func<EntityRepository?> _world;
    private Entity _entity = Entity.Null;
    private string _seed = "";

    /// <summary>The screen rectangles of what the last <see cref="Draw"/> placed, by name — <c>species</c>, <c>count</c>, <c>seed</c>, <c>dice</c>, <c>enclosed</c> (the frame rail's measure).</summary>
    public Dictionary<string, (Vector2 Min, Vector2 Max)> Rects { get; } = new();

    public AnimalAreaDetailsView(Func<EntityRepository?> world) => _world = world ?? throw new ArgumentNullException(nameof(world));

    public void Draw(DetailsContext context, string idScope)
    {
        if (context.Entities is not { Count: 1 } || _world() is not { } world) return;
        var e = context.Entities[0];
        if (!world.IsAlive(e) || !world.IsComponentTypeRegistered<AnimalArea>() || !world.HasComponent<AnimalArea>(e)) { ImGui.TextDisabled("Select one animal area."); return; }
        if (e != _entity) { _entity = e; _seed = world.GetComponentRO<AnimalArea>(e).Seed.ToString(); }
        var area = world.GetComponentRO<AnimalArea>(e);
        Rects.Clear();
        ImGui.PushID(idScope + "/animalarea");

        FormGrid.BeginField(Form, "Species");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##species", AnimalAreaOps.NameOf(area.Species)))
        {
            foreach (var s in AnimalAreaOps.Species)
                if (ImGui.Selectable(AnimalAreaOps.NameOf(s) + "##s_" + (int)s, s == area.Species)) AnimalAreaOps.SetSpecies(world, e, s);
            ImGui.EndCombo();
        }
        Rects["species"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        FormGrid.EndField();

        FormGrid.BeginField(Form, "Count");
        int count = area.Count;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputInt("##count", ref count, 1, 10) && count != area.Count) AnimalAreaOps.SetCount(world, e, count);
        Rects["count"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        FormGrid.EndField();

        FormGrid.BeginField(Form, "Seed");
        var dice = Icons.Get("terrain/reroll");
        ImGui.SetNextItemWidth(-(GlyphWidgets.WidthOf(dice) + ImGui.GetStyle().ItemSpacing.X));
        if (!ImGui.IsAnyItemActive()) _seed = area.Seed.ToString();
        ImGui.InputText("##seed", ref _seed, 10, ImGuiInputTextFlags.CharsDecimal);
        if (ImGui.IsItemDeactivatedAfterEdit() && uint.TryParse(_seed, out uint typed)) AnimalAreaOps.SetSeed(world, e, typed);
        Rects["seed"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine();
        if (GlyphWidgets.GlyphButton("##reroll", dice, "Re-roll the placement: a new seed, same place")) AnimalAreaOps.Reroll(world, e);
        Rects["dice"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        FormGrid.EndField();

        FormGrid.BeginField(Form, "Enclosed");
        bool enclosed = area.Enclosed != 0;
        if (ImGui.Checkbox("##enclosed", ref enclosed)) AnimalAreaOps.SetEnclosed(world, e, enclosed);
        Rects["enclosed"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine();
        ImGui.TextDisabled("never leave (on for pigs and goats by default)");
        FormGrid.EndField();
        ImGui.PopID();
    }

    private const string Form = "animal.area";

    public void Dispose() { }
}

/// <summary>The Animal area Details view's descriptor — offered for exactly one selected animal area, while authoring.</summary>
public static class AnimalAreaDetailsViewDescriptor
{
    public const string ViewId = "details.animalarea";
    /// <summary>Beside the Block view (37).</summary>
    public const int Rank = 38;

    public static DetailsViewDescriptor For(Func<EntityRepository?> world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var instance = new AnimalAreaDetailsView(world);
        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Animal area",
            Rank:      Rank,
            AppliesTo: ctx => DetailsViewPredicates.ExactlyOneEntity(ctx) && world() is { } w && w.IsAlive(ctx.Entities[0]),
            Create:    () => instance)
        {
            Requires     = new[] { typeof(AnimalArea) },
            Availability = new Hrot.Common.Actions.ActionAvailability(Hrot.Common.Actions.ActionModes.InEdit),
        };
    }
}
