using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Hrot.UI.Common.AddEntity;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐ CE-4624 (minefields MS-U3; docs/designs/minefields/minefields-visuals.svg panel "Details - the Minefield form", DESIGN_Minefields MR-8 / MR-18 / MF-12) — the <b>Minefield</b> tab of Details for one placed minefield: Type (AP / AT /
/// Mixed), Spacing, Row orientation, Seed (+ the dice), Anti-handling, Known at start (a box per side, in its colour), the derived Mines line, then the LANES (width · length · cleared by · known to · delete) and Add lane.
/// ⭐ A Details view on the existing seam (<see cref="DetailsViewDescriptor"/>, beside the animal area's), label-first rows (<see cref="FormGrid"/>), every edit ONE undo step (<see cref="MinefieldOps"/>). The OUTLINE is not here
/// (MR-8): it is the entity's one editable polyline on the map. ⭐ 0 B on a steady frame: the Mines line and the lane texts are rebuilt only when the field changes.
/// </summary>
public sealed class MinefieldDetailsView : IDetailsViewInstance
{
    private readonly Func<EntityRepository?> _world;
    private readonly Action<Entity>? _armLane;
    private Entity _entity = Entity.Null;
    private string _seed = "";
    // the cached texts (rebuilt only when the field changes)
    private Minefield _shown; private bool _hasShown;
    private MinefieldOps.MineCounts _countsShown; private bool _hasCounts;
    private string _mines = "";
    private readonly string[] _laneText = new string[Minefield.MaxLanes];
    private int _struckShown = -1; private string _lock = "";

    /// <summary>The screen rectangles of what the last <see cref="Draw"/> placed, by name — <c>type</c> <c>spacing</c> <c>orientation</c> <c>seed</c> <c>dice</c> <c>antihandling</c> <c>side0…</c> <c>mines</c> <c>lane{i}</c> <c>lane{i}.delete</c> <c>addlane</c> (the frame rail's measure).</summary>
    public Dictionary<string, (Vector2 Min, Vector2 Max)> Rects { get; } = new();

    /// <summary>The sides a field can be known to, by <c>ForceId</c> (0 neutral, 1 friend, 2 hostile): the name and the colour the mockup draws them in.</summary>
    public static readonly (int Id, string Name, Vector4 Colour)[] Sides =
    {
        (1, "Blue", IconPalette.Blue), (2, "Red", IconPalette.Red), (0, "Green", IconPalette.Green),
    };

    public MinefieldDetailsView(Func<EntityRepository?> world, Action<Entity>? armLane = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _armLane = armLane;
    }

    private static string TypeName(MineType t) => t switch { MineType.AntiPersonnel => "AP (anti-personnel)", MineType.AntiTank => "AT (anti-tank)", _ => "Mixed (AP + AT)" };

    private static readonly MineType[] Types = { MineType.AntiPersonnel, MineType.AntiTank, MineType.Mixed };

    private float Scale => ImGui.GetFontSize() / FormGrid.ReferenceFontSize;

    public void Draw(DetailsContext context, string idScope)
    {
        if (context.Entities is not { Count: 1 } || _world() is not { } world) return;
        var e = context.Entities[0];
        if (!MinefieldOps.Is(world, e)) { ImGui.TextDisabled("Select one minefield."); return; }
        if (e != _entity) { _entity = e; _hasShown = false; _hasCounts = false; _struckShown = -1; _seed = world.GetComponentRO<Minefield>(e).Seed.ToString(CultureInfo.InvariantCulture); }
        var f = world.GetComponentRO<Minefield>(e);
        Rects.Clear();
        ImGui.PushID(idScope + "/minefield");
        float s = Scale;

        SectionHeader.Draw("minefield.head", "Minefield", "minefield/field", ImGuiTreeNodeFlags.DefaultOpen);

        int struck = MinefieldOps.StruckCount(world, e);
        if (struck != _struckShown)
        {
            _struckShown = struck;
            _lock = struck > 0 && MinefieldOps.ShapeLock(world, e, out var reason) ? reason : "";
        }
        if (_lock.Length > 0)
        {
            var warn = Icons.Get("form/locked");
            ImGui.TextColored(warn.Color, warn.Glyph); ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, IconPalette.Pending); ImGui.TextWrapped(_lock); ImGui.PopStyleColor();
        }

        FormGrid.BeginField(Form, "Type");
        ImGui.SetNextItemWidth(210f * s);
        if (ImGui.BeginCombo("##type", TypeName(f.Type)))
        {
            for (int i = 0; i < Types.Length; i++)
                if (ImGui.Selectable(TypeName(Types[i]), Types[i] == f.Type)) MinefieldOps.SetType(world, e, Types[i]);
            ImGui.EndCombo();
        }
        Rects["type"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine(); ImGui.TextDisabled("AP: any person or vehicle · AT: vehicles only");
        FormGrid.EndField();

        FormGrid.BeginField(Form, "Spacing");
        float spacing = f.SpacingM;
        ImGui.SetNextItemWidth(90f * s);
        ImGui.InputFloat("##spacing", ref spacing, 0f, 0f, "%.0f m");
        if (ImGui.IsItemDeactivatedAfterEdit() && spacing != f.SpacingM) MinefieldOps.SetSpacing(world, e, spacing);   // committed when the field is left (one step, not one per keystroke)
        Rects["spacing"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine(); ImGui.TextDisabled("between mines in a row and between rows (staggered)");
        FormGrid.EndField();

        FormGrid.BeginField(Form, "Row orientation");
        float bearing = f.OrientationDeg;
        ImGui.SetNextItemWidth(90f * s);
        ImGui.InputFloat("##orientation", ref bearing, 0f, 0f, "%.0f°");
        if (ImGui.IsItemDeactivatedAfterEdit() && bearing != f.OrientationDeg) MinefieldOps.SetOrientation(world, e, bearing);
        Rects["orientation"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine(); ImGui.TextDisabled("the rows' bearing (0 = east, counter-clockwise)");
        FormGrid.EndField();

        FormGrid.BeginField(Form, "Seed");
        var dice = Icons.Get("terrain/reroll");
        ImGui.SetNextItemWidth(90f * s);
        if (!ImGui.IsAnyItemActive()) _seed = f.Seed.ToString(CultureInfo.InvariantCulture);
        ImGui.InputText("##seed", ref _seed, 10, ImGuiInputTextFlags.CharsDecimal);
        if (ImGui.IsItemDeactivatedAfterEdit() && uint.TryParse(_seed, NumberStyles.None, CultureInfo.InvariantCulture, out uint typed)) MinefieldOps.SetSeed(world, e, typed);
        Rects["seed"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine();
        if (GlyphWidgets.GlyphButton("##reroll", dice, "Re-roll the jitter: a new seed, same field")) MinefieldOps.Reroll(world, e);
        Rects["dice"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine(); ImGui.TextDisabled("jitter only (0 = exact pattern)");
        FormGrid.EndField();

        FormGrid.BeginField(Form, "Anti-handling");
        bool anti = f.AntiHandling != 0;
        if (ImGui.Checkbox("##anti", ref anti)) MinefieldOps.SetAntiHandling(world, e, anti);
        Rects["antihandling"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine(); ImGui.TextDisabled("a plough / flail misses more (MR-15)");
        FormGrid.EndField();

        FormGrid.BeginField(Form, "Known at start");
        for (int i = 0; i < Sides.Length; i++)
        {
            var (id, name, colour) = Sides[i];
            if (i > 0) ImGui.SameLine();
            bool on = (f.KnownAtStartSides & (1 << id)) != 0;
            ImGui.PushID(id);
            if (ImGui.Checkbox("##side", ref on)) MinefieldOps.SetKnownAtStart(world, e, id, on);
            Rects[SideKey(i)] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.PopID();
            ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X); ImGui.TextColored(colour, name);
        }
        ImGui.SameLine(); ImGui.TextDisabled("a side that knows it avoids it (MF-15)");
        FormGrid.EndField();

        FormGrid.BeginField(Form, "Mines");
        var counts = MinefieldOps.Counts(world, e);
        if (!_hasCounts || counts != _countsShown) { _countsShown = counts; _hasCounts = true; _mines = MinesText(counts); }
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(_mines);
        Rects["mines"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        FormGrid.EndField();

        ImGui.Spacing();
        SectionHeader.Draw("minefield.lanes", "Lanes", "minefield/lanes", ImGuiTreeNodeFlags.DefaultOpen);
        if (!_hasShown || !MinefieldOps.Same(in f, in _shown)) { _shown = f; _hasShown = true; for (int i = 0; i < Minefield.MaxLanes; i++) _laneText[i] = i < f.LaneCount ? LaneText(in f, i) : ""; }
        var trash = Icons.Get("terrain/delete");
        int delete = -1;
        for (int i = 0; i < f.LaneCount; i++)
        {
            var lane = f.Lane(i);
            ImGui.PushID(i);
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled((i + 1).ToString(CultureInfo.InvariantCulture)); ImGui.SameLine(26f * s);
            ImGui.TextUnformatted(_laneText[i]);
            Rects[LaneKey(i)] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.SameLine(); ImGui.TextDisabled("cleared by");
            ImGui.SameLine(); SideNames(lane.ClearedBySidePlus1 == 0 ? 0 : 1 << (lane.ClearedBySidePlus1 - 1), "authored");
            ImGui.SameLine(); ImGui.TextDisabled("known to");
            ImGui.SameLine(); SideNames(lane.KnownToSides, "nobody");
            ImGui.SameLine(ImGui.GetWindowWidth() - GlyphWidgets.WidthOf(trash) - ImGui.GetStyle().WindowPadding.X);
            if (GlyphWidgets.GlyphButton("##del", trash, "Delete this lane")) delete = i;
            Rects[LaneDeleteKeys[i]] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            ImGui.PopID();
        }
        if (delete >= 0) MinefieldOps.DeleteLane(world, e, delete);

        bool full = f.LaneCount >= Minefield.MaxLanes || _armLane is null;
        if (full) ImGui.BeginDisabled();
        if (ImGui.Button("       Add lane") && _armLane is not null) _armLane(e);   // the leading blanks leave room for the green plus (the mockup's glyph button)
        Rects["addlane"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        {
            var min = ImGui.GetItemRectMin();
            ImGui.GetWindowDrawList().AddText(new Vector2(min.X + ImGui.GetStyle().FramePadding.X + 2f * s, min.Y + ImGui.GetStyle().FramePadding.Y), ImGui.ColorConvertFloat4ToU32(full ? IconPalette.Muted : IconPalette.Green), KitGlyph.Plus);
        }
        if (full) ImGui.EndDisabled();
        ImGui.SameLine(); ImGui.TextDisabled(f.LaneCount >= Minefield.MaxLanes ? "a field holds 8 lanes" : "arms the area tool in line mode: two clicks (MR-18)");
        ImGui.PopID();
    }

    // the keys are built once (a steady frame allocates nothing)
    private static readonly string[] SideKeys = { "side0", "side1", "side2" };
    private static readonly string[] LaneKeys = BuildLaneKeys();
    private static string[] BuildLaneKeys() { var k = new string[Minefield.MaxLanes]; for (int i = 0; i < k.Length; i++) k[i] = "lane" + i.ToString(CultureInfo.InvariantCulture); return k; }
    private static string SideKey(int i) => SideKeys[i];
    private static string LaneKey(int i) => LaneKeys[i];
    private static readonly string[] LaneDeleteKeys = BuildDeleteKeys();
    private static string[] BuildDeleteKeys() { var k = new string[Minefield.MaxLanes]; for (int i = 0; i < k.Length; i++) k[i] = "lane" + i.ToString(CultureInfo.InvariantCulture) + ".delete"; return k; }

    private static void SideNames(int mask, string none)
    {
        bool any = false;
        for (int i = 0; i < Sides.Length; i++)
        {
            if ((mask & (1 << Sides[i].Id)) == 0) continue;
            if (any) ImGui.SameLine(0f, 4f);
            ImGui.TextColored(Sides[i].Colour, Sides[i].Name);
            any = true;
        }
        if (!any) ImGui.TextDisabled(none);
    }

    /// <summary>"1 gone off" style line of the mockup: <c>N (A AP, B AT) · S gone off · C cleared</c>, invariant culture.</summary>
    public static string MinesText(in MinefieldOps.MineCounts c)
        => string.Create(CultureInfo.InvariantCulture, $"{c.Total} ({c.AntiPersonnel} AP, {c.AntiTank} AT) · {c.Struck} gone off · {c.Cleared} cleared");

    private static string LaneText(in Minefield f, int i)
        => string.Create(CultureInfo.InvariantCulture, $"{f.Lane(i).Width:0.#} m · {MinefieldOps.LaneLength(in f, i):0} m long");

    private const string Form = "minefield.form";

    public void Dispose() { }
}

/// <summary>The Minefield Details view's descriptor — offered for exactly one selected minefield, while authoring.</summary>
public static class MinefieldDetailsViewDescriptor
{
    public const string ViewId = "details.minefield";
    /// <summary>Beside the animal area's view (38).</summary>
    public const int Rank = 39;

    /// <param name="world">The editor's world.</param>
    /// <param name="armLane">Arms the area tool as a LINE for a lane of the field (the host's spawn adapter); null disables Add lane.</param>
    public static DetailsViewDescriptor For(Func<EntityRepository?> world, Action<Entity>? armLane = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        var instance = new MinefieldDetailsView(world, armLane);
        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Minefield",
            Rank:      Rank,
            AppliesTo: ctx => DetailsViewPredicates.ExactlyOneEntity(ctx) && world() is { } w && w.IsAlive(ctx.Entities[0]),
            Create:    () => instance)
        {
            Requires     = new[] { typeof(Minefield) },
            Availability = new Hrot.Common.Actions.ActionAvailability(Hrot.Common.Actions.ActionModes.InEdit),
        };
    }
}
