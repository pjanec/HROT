using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Motion;
using Hrot.UI.Common.AddEntity;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐ CE-4647 (AC-15 step 6; docs/designs/air-attack/air-combat-arena.svg, "Details - Air Arena") — the <b>Air Arena</b> tab of Details for one placed arena: Geometry (Centre "x … y …", Radius, Floor, Ceiling — the four numbers the gizmo drags) and Show rules (Max shot range,
/// Stalemate seconds, Lapses off). ⭐ A Details view on the existing seam beside the minefield's, label-first rows (<see cref="FormGrid"/>), every edit ONE undo step through <see cref="AirArenaOps"/> (committed when the field is left, not per keystroke).
/// </summary>
public sealed class AirArenaDetailsView : IDetailsViewInstance
{
    private readonly Func<EntityRepository?> _world;

    /// <summary>The screen rectangles of what the last <see cref="Draw"/> placed, by name — <c>centre</c> <c>radius</c> <c>floor</c> <c>ceiling</c> <c>shot</c> <c>stalemate</c> <c>lapses</c> (the frame rail's measure).</summary>
    public Dictionary<string, (Vector2 Min, Vector2 Max)> Rects { get; } = new();

    public AirArenaDetailsView(Func<EntityRepository?> world) => _world = world ?? throw new ArgumentNullException(nameof(world));

    private static readonly Dictionary<string, string> Ids = new() { ["radius"] = "##radius", ["floor"] = "##floor", ["ceiling"] = "##ceiling", ["shot"] = "##shot", ["stalemate"] = "##stalemate" };

    private float Scale => ImGui.GetFontSize() / FormGrid.ReferenceFontSize;
    private const string Form = "airarena.form";
    /// <summary>The mockup's control width (px at the reference font): every framed control is this wide.</summary>
    public const float Width = 200f;

    private bool Field(string key, string label, ref float value, string format, string hint, float width)
    {
        FormGrid.BeginField(Form, label);
        ImGui.SetNextItemWidth(width * Scale);
        float v = value;
        ImGui.InputFloat(Ids[key], ref v, 0f, 0f, format);   // ids built once: a steady frame allocates nothing
        bool commit = ImGui.IsItemDeactivatedAfterEdit() && v != value;   // one step when the field is left
        Rects[key] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (hint.Length > 0) { ImGui.SameLine(); ImGui.TextDisabled(hint); }
        FormGrid.EndField();
        value = v;
        return commit;
    }

    public void Draw(DetailsContext context, string idScope)
    {
        if (context.Entities is not { Count: 1 } || _world() is not { } world) return;
        var e = context.Entities[0];
        if (!AirArenaOps.Is(world, e)) { ImGui.TextDisabled("Select one air arena."); return; }
        var a = world.GetComponentRO<AirArena>(e);
        Rects.Clear();
        ImGui.PushID(idScope + "/airarena");

        SectionHeader.Draw("airarena.geometry", "Geometry", "airarena/geometry", ImGuiTreeNodeFlags.DefaultOpen);
        FormGrid.BeginField(Form, "Centre");   // one framed control, "x … y …" (the mockup's row)
        ImGui.SetNextItemWidth(Width * Scale);
        var xy = new Vector2(a.Centre.X, a.Centre.Y);
        ImGui.InputFloat2("##centre", ref xy, "%.0f");
        bool moved = ImGui.IsItemDeactivatedAfterEdit() && (xy.X != a.Centre.X || xy.Y != a.Centre.Y);
        Rects["centre"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine(); ImGui.TextDisabled("m · the gizmo's centre handle drags both");
        FormGrid.EndField();
        if (moved) AirArenaOps.SetCentre(world, e, xy);
        float r = a.RadiusM;
        if (Field("radius", "Radius", ref r, "%.0f m", "the ring (500 m … 30 km)", Width)) AirArenaOps.SetRadius(world, e, r);
        float fl = a.FloorAglM;
        if (Field("floor", "Floor", ref fl, "%.0f m", "above the ground at the centre: no pilot below it", Width)) AirArenaOps.SetFloor(world, e, fl);
        float ce = a.CeilingAglM;
        if (Field("ceiling", "Ceiling", ref ce, "%.0f m", "at least 200 m above the floor", Width)) AirArenaOps.SetCeiling(world, e, ce);

        ImGui.Spacing();
        SectionHeader.Draw("airarena.rules", "Show rules", "airarena/rules", ImGuiTreeNodeFlags.DefaultOpen);
        float shot = a.MaxShotRangeM > 0f ? a.MaxShotRangeM : AirArena.DefaultMaxShotRangeM;
        if (Field("shot", "Max shot range", ref shot, "%.0f m", "no missile beyond it, whatever the zone says", Width)) AirArenaOps.SetMaxShotRange(world, e, shot);
        float st = a.StalemateSeconds > 0f ? a.StalemateSeconds : AirArena.DefaultStalemateSeconds;
        if (Field("stalemate", "Stalemate", ref st, "%.0f s", "no hit this long ⇒ the pilots start to lapse", Width)) AirArenaOps.SetStalemate(world, e, st);
        FormGrid.BeginField(Form, "Lapses off");
        bool off = a.LapsesOff != 0;
        if (ImGui.Checkbox("##lapses", ref off)) AirArenaOps.SetLapsesOff(world, e, off);
        Rects["lapses"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine(); ImGui.TextDisabled("a feature test where skill alone decides");
        FormGrid.EndField();
        ImGui.PopID();
    }

    public void Dispose() { }
}

/// <summary>The Air Arena Details view's descriptor — offered for exactly one selected arena, while authoring.</summary>
public static class AirArenaDetailsViewDescriptor
{
    public const string ViewId = "details.airarena";
    /// <summary>Beside the minefield's view (39).</summary>
    public const int Rank = 40;

    public static DetailsViewDescriptor For(Func<EntityRepository?> world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var instance = new AirArenaDetailsView(world);
        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Air Arena",
            Rank:      Rank,
            AppliesTo: ctx => DetailsViewPredicates.ExactlyOneEntity(ctx) && world() is { } w && w.IsAlive(ctx.Entities[0]),
            Create:    () => instance)
        {
            Requires     = new[] { typeof(AirArena) },
            Availability = new Hrot.Common.Actions.ActionAvailability(Hrot.Common.Actions.ActionModes.InEdit),
        };
    }
}
