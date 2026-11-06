using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Vis2D.Gizmos;
using StructEdit.Core;

namespace Fdp.Presentation.Editing;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>CE-4206 R2 — the form an <see cref="AreaLink"/> leaf shows.</summary>
public enum AreaLeafForm { Circle, Rectangle, Entity }

/// <summary>
/// ⭐⭐ CE-4206 R1 (<c>docs/blueprints/DESIGN_Map_Picking_Unification.md</c> §6.2, F1) — the ONE drawer per reference TYPE, moved out of
/// <see cref="ComponentEditDrawer"/> (which dispatches by <c>node.ClrType</c>): <see cref="EntityLink"/> (<c>[Exact | Key]</c>, F3 / F4),
/// <see cref="PointLink"/> (<c>[Coordinates | Entity]</c>, F5), and the internal <see cref="EntityRef"/> / <see cref="WorldPoint"/> (unchanged).
/// Names, GUIDs and keys come from the pick context's <see cref="IMapPickContext.References"/> (<see cref="LiveRefScope"/>); without one the leaves
/// draw the plain forms. The logic that decides WHAT a pick or an edit writes is in the public static helpers (no ImGui), which the rails call.
/// </summary>
public static class ReferenceLeafDrawers
{
    /// <summary>Does this class draw a field of <paramref name="type"/>?</summary>
    public static bool Handles(Type type)
        => type == typeof(EntityLink) || type == typeof(PointLink) || type == typeof(EntityRef) || type == typeof(WorldPoint) || type == typeof(AreaLink)
           || type == typeof(Bearing) || type == typeof(RefKey);

    /// <summary>The words shown beside an entity written as a bare id.</summary>
    public const string NoStableId = "no stable id — will not survive a save";

    /// <summary>UI state the drawer keeps per field path: the form a person switched to, the key text being typed, a note.</summary>
    public sealed class State
    {
        internal readonly Dictionary<string, bool> KeyForm = new();
        internal readonly Dictionary<string, string> KeyText = new();
        internal readonly Dictionary<string, bool> PointEntityForm = new();
        internal readonly Dictionary<string, string> Notice = new();
        internal readonly Dictionary<string, AreaLeafForm> AreaForm = new();
    }

    // ═══ the pure rules (rails call these) ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>F3 — what an entity pick writes into an <see cref="EntityLink"/>: <c>Exact</c> = the picked entity's GUID; no GUID (a bullet, an
    /// unsaved helper) or no scope ⇒ the bare id.</summary>
    public static EntityLink PickToLink(EntityRef picked, LiveRefScope? refs)
        => refs != null && refs.TryGuidOf(picked, out var g) ? new EntityLink(picked, RefSpec.ByExact(g)) : new EntityLink(picked);

    /// <summary>Does the link need the "no stable id" warning: a bare id of an entity that has no GUID (only judged with a scope to ask).</summary>
    public static bool NeedsStableIdWarning(EntityLink link, LiveRefScope? refs)
        => refs != null && !link.Ref.IsNone && link.Spec.IsNone && !refs.TryGuidOf(link.Ref, out _);

    /// <summary>F4 Key form — the link a key text writes: <c>ByKey(text)</c> with the entity that key names here (none when nothing does).
    /// <c>false</c> (with the refusal) when the text is not a legal key (empty, over 31 bytes).</summary>
    public static bool TryKeyToLink(string text, LiveRefScope? refs, out EntityLink link, out string? refusal)
    {
        link = default; refusal = null;
        try
        {
            var spec = RefSpec.ByKey(text);
            EntityRef resolved = EntityRef.None;
            refs?.TryResolve(spec, out resolved);
            link = new EntityLink(resolved, spec);
            return true;
        }
        catch (ArgumentException e) { refusal = e.Message.Split('\n')[0]; return false; }
    }

    /// <summary>The red line under a key box: why the key does not name exactly one entity here; <c>null</c> when it does (or no scope to ask).</summary>
    public static string? KeyProblem(string text, LiveRefScope? refs)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (!TryKeyToLink(text, refs, out var link, out var refusal)) return refusal;
        if (refs == null) return null;
        string key = link.Spec.KeyText;
        if (refs.HasDuplicate(key)) return $"key '{key}' is not unique";
        return link.Ref.IsNone ? $"no entity with key '{key}' here" : null;
    }

    /// <summary>F4 — switching Exact → Key: the entity's <see cref="RefKey"/> prefilled when it has one (else the link is left as it is).</summary>
    public static EntityLink SwitchToKey(EntityLink current, LiveRefScope? refs, out string text)
    {
        text = current.Spec.Form == RefForm.Key ? current.Spec.KeyText : "";
        if (refs != null && !current.Ref.IsNone && refs.TryKeyOf(current.Ref, out var key)) text = key;
        return text.Length > 0 && TryKeyToLink(text, refs, out var link, out _) ? new EntityLink(current.Ref.IsNone ? link.Ref : current.Ref, link.Spec) : current;
    }

    /// <summary>F4 — switching Key → Exact: the resolved entity's GUID when it has one, else the bare id.</summary>
    public static EntityLink SwitchToExact(EntityLink current, LiveRefScope? refs)
    {
        var entity = current.Ref;
        if (entity.IsNone && refs != null && current.Spec.Form == RefForm.Key) refs.TryResolve(current.Spec, out entity);
        return entity.IsNone ? EntityLink.None : PickToLink(entity, refs);
    }

    /// <summary>F5 — the <see cref="PointLink"/> an entity form writes: the entity's authored <paramref name="spec"/> and its position AT THIS MOMENT (a snapshot,
    /// SR-130 — it does not follow the entity). No position known ⇒ the previous local is kept.</summary>
    public static PointLink PointFromSpec(RefSpec spec, EntityRef entity, WorldPoint previousLocal, LiveRefScope? refs)
    {
        var at = previousLocal;
        if (refs != null)
        {
            if (entity.IsNone) refs.TryResolve(spec, out entity);
            if (!entity.IsNone && refs.TryPositionOf(entity, out var p)) at = p;
        }
        return new PointLink(at, spec);
    }

    /// <summary>F5 — an entity pick into the Entity form: its GUID spec + position. An entity with no GUID cannot be written as a reference, so its position is
    /// copied as coordinates (<paramref name="stable"/> false — the leaf says so).</summary>
    public static PointLink PointFromPick(EntityRef picked, WorldPoint previousLocal, LiveRefScope? refs, out bool stable)
    {
        var link = PickToLink(picked, refs);
        stable = !link.Spec.IsNone;
        return PointFromSpec(link.Spec, picked, previousLocal, refs);
    }

    // ═══ AreaLink (CE-4206 R2, F6) ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The form of an <see cref="AreaLink"/> value: a circle, a rectangle, or an area ENTITY (a key / GUID / id). A value with nothing in it reads as a circle.</summary>
    public static AreaLeafForm AreaFormOf(AreaLink area)
        => area.Spec.Form == RefForm.Rectangle ? AreaLeafForm.Rectangle
         : area.Spec.Form == RefForm.Circle || area.IsNone ? AreaLeafForm.Circle
         : AreaLeafForm.Entity;

    /// <summary>
    /// F6 — switching form keeps the CENTRE where it can: circle ⇄ rectangle keep <c>Center</c> (a circle's diameter becomes a square's side, a rectangle's longer half becomes
    /// the radius, the heading is kept); from an area entity the centre is its position when a scope can say. To Entity: nothing chosen yet.
    /// </summary>
    public static AreaLink SwitchAreaForm(AreaLink current, AreaLeafForm to, LiveRefScope? refs)
    {
        var centre = current.IsShape ? current.Center : default;
        if (!current.IsShape && !current.Area.IsNone && refs != null && refs.TryPositionOf(current.Area, out var at)) centre = at;
        switch (to)
        {
            case AreaLeafForm.Circle:
                return AreaLink.Circle(centre, current.Spec.Form == RefForm.Circle ? current.Size.X
                    : current.Spec.Form == RefForm.Rectangle ? MathF.Max(current.Size.X, current.Size.Y) * 0.5f : AreaShapeHandles.DefaultRadius);
            case AreaLeafForm.Rectangle:
                return current.Spec.Form == RefForm.Rectangle ? current
                    : current.Spec.Form == RefForm.Circle ? AreaLink.Rectangle(centre, current.Size.X * 2f, current.Size.X * 2f, default)
                    : AreaLink.Rectangle(centre, AreaShapeHandles.DefaultRadius * 2f, AreaShapeHandles.DefaultRadius * 2f, default);
            default:
                return default(AreaLink);
        }
    }

    /// <summary>The area an area ENTITY field holds as the EntityLink the Exact / Key controls edit.</summary>
    public static EntityLink AreaEntityAsLink(AreaLink area, LiveRefScope? refs)
    {
        if (area.IsShape) return EntityLink.None;
        var entity = area.Area;
        if (entity.IsNone && refs != null && !area.Spec.IsNone) refs.TryResolve(area.Spec, out entity);
        return new EntityLink(entity, area.Spec);
    }

    /// <summary>The area value an Exact / Key edit of the entity form writes.</summary>
    public static AreaLink AreaFromEntityLink(EntityLink link) => AreaLink.OfEntity(link.Ref, link.Spec);

    private static bool DrawAreaLink(EditNode node, float width, IMapPickContext? ctx, State st)
    {
        var area = node.Binding?.GetBoxed() is AreaLink a ? a : default;
        string path = node.JsonPath;
        var id = node.Id.Value;
        var refs = ctx?.References;
        var form = st.AreaForm.TryGetValue(path, out var o) ? o : AreaFormOf(area);
        bool changed = false;

        // ── the three-way switch ──────────────────────────────────────────────────────────────────────────────────────────
        foreach (var (f, label) in new[] { (AreaLeafForm.Circle, "Circle"), (AreaLeafForm.Rectangle, "Rectangle"), (AreaLeafForm.Entity, "Area entity") })
        {
            if (ImGuiApi.RadioButton($"{label}##af{f}{id}", form == f) && form != f)
            {
                st.AreaForm[path] = form = f;
                area = SwitchAreaForm(area, f, refs);
                changed = true;
            }
            ImGuiApi.SameLine(0f, 4f);
        }

        if (form == AreaLeafForm.Entity)
        {
            ImGuiApi.NewLine();
            var link = AreaEntityAsLink(area, refs);
            if (DrawEntityLinkCore(node, ref link, width, ctx, st))
            {
                area = AreaFromEntityLink(link);
                changed = true;
            }
        }
        else
        {
            ImGuiApi.NewLine();
            var c = new Vector2(area.IsShape ? area.Center.X : 0f, area.IsShape ? area.Center.Y : 0f);
            float third = Math.Max((width - 32f) / 5f, 40f);
            bool edited = false;
            ImGuiApi.SetNextItemWidth(third); edited |= ImGuiApi.InputFloat($"##cx{id}", ref c.X, 0, 0, "x %.1f");
            ImGuiApi.SameLine(0f, 4f);
            ImGuiApi.SetNextItemWidth(third); edited |= ImGuiApi.InputFloat($"##cy{id}", ref c.Y, 0, 0, "y %.1f");
            ImGuiApi.SameLine(0f, 4f);
            if (form == AreaLeafForm.Circle)
            {
                float r = area.Spec.Form == RefForm.Circle ? area.Size.X : 0f;
                ImGuiApi.SetNextItemWidth(third); edited |= ImGuiApi.InputFloat($"##r{id}", ref r, 0, 0, "r %.1f m");
                if (edited) { area = AreaLink.Circle(new WorldPoint(c.X, c.Y, area.IsShape ? area.Center.Z : 0f), MathF.Max(0f, r)); changed = true; }
            }
            else
            {
                float w = area.Spec.Form == RefForm.Rectangle ? area.Size.X : 0f, l = area.Spec.Form == RefForm.Rectangle ? area.Size.Y : 0f;
                float h = area.Spec.Form == RefForm.Rectangle ? area.Heading.Degrees : 0f;
                ImGuiApi.SetNextItemWidth(third); edited |= ImGuiApi.InputFloat($"##w{id}", ref w, 0, 0, "w %.1f");
                ImGuiApi.SameLine(0f, 4f);
                ImGuiApi.SetNextItemWidth(third); edited |= ImGuiApi.InputFloat($"##l{id}", ref l, 0, 0, "l %.1f");
                ImGuiApi.SameLine(0f, 4f);
                ImGuiApi.SetNextItemWidth(third); edited |= ImGuiApi.InputFloat($"##h{id}", ref h, 0, 0, "hdg %.0f°");
                if (edited) { area = AreaLink.Rectangle(new WorldPoint(c.X, c.Y, area.IsShape ? area.Center.Z : 0f), MathF.Max(0f, w), MathF.Max(0f, l), new Bearing(h)); changed = true; }
            }

            // ── Edit on map ───────────────────────────────────────────────────────────────────────────────────────────────
            if (ctx != null)
            {
                ImGuiApi.SameLine();
                if (ctx.IsPickPendingFor(path))
                    ImGuiApi.TextColored(new Vector4(1f, 1f, 0f, 1f), "[Editing on map… Enter = done, Esc = cancel]");
                else if (ImGuiApi.Button($"Edit on map##am{id}"))
                    ctx.RequestAreaEdit(path, area.IsShape ? area : form == AreaLeafForm.Rectangle ? SwitchAreaForm(area, AreaLeafForm.Rectangle, refs) : default);
                if (ctx.TryConsumeAreaEdit(path, out var edited2))
                {
                    area = edited2;
                    st.AreaForm[path] = AreaFormOf(area);
                    changed = true;
                }
            }
        }

        if (changed) node.Binding?.SetBoxed(area);
        return changed;
    }

    // ═══ CE-4206 R3 — F7 Bearing, F8 Show on map, F9a RefKey ═══════════════════════════════════════════════════════════════════

    /// <summary>The place a reference value stands for, for <i>Show on map</i>: an entity (centred by its position), a point, or nothing (a value with no place).</summary>
    public static (EntityRef Entity, WorldPoint? Point) ShowTarget(object? value, LiveRefScope? refs)
    {
        switch (value)
        {
            case EntityRef e: return (e, null);
            case EntityLink l:
                if (!l.Ref.IsNone) return (l.Ref, null);
                return refs != null && !l.Spec.IsNone && refs.TryResolve(l.Spec, out var r) ? (r, null) : (EntityRef.None, null);
            case WorldPoint p: return (EntityRef.None, p);
            case PointLink pl: return (EntityRef.None, pl.Local);
            case AreaLink a when a.IsShape: return (EntityRef.None, a.Center);
            case AreaLink a:
                if (!a.Area.IsNone) return (a.Area, null);
                return refs != null && !a.Spec.IsNone && refs.TryResolve(a.Spec, out var ar) ? (ar, null) : (EntityRef.None, null);
            default: return (EntityRef.None, null);
        }
    }

    /// <summary>F8 — centres the map on <paramref name="value"/> through the pick context; <c>false</c> when the context cannot or the value stands for no place.</summary>
    public static bool ShowOnMap(object? value, IMapPickContext? ctx)
    {
        if (ctx is not { CanShowOnMap: true }) return false;
        var (entity, point) = ShowTarget(value, ctx.References);
        if (!entity.IsNone) { ctx.ShowOnMap(entity); return true; }
        if (point is { } p) { ctx.ShowOnMap(p); return true; }
        return false;
    }

    private static void DrawShowOnMap(EditNode node, IMapPickContext? ctx)
    {
        if (ctx is not { CanShowOnMap: true }) return;
        var (entity, point) = ShowTarget(node.Binding?.GetBoxed(), ctx.References);
        if (entity.IsNone && point is null) return;
        ImGuiApi.SameLine();
        if (ImGuiApi.SmallButton($"Show##sm{node.Id.Value}")) ShowOnMap(node.Binding?.GetBoxed(), ctx);
        if (ImGuiApi.IsItemHovered()) ImGuiApi.SetTooltip("Show on map");
    }

    // ── F7: Bearing — degrees + a compass dial ───────────────────────────────────────────────────────────────────────────────

    private static bool DrawBearing(EditNode node, float width)
    {
        float deg = node.Binding?.GetBoxed() is Bearing b ? b.Degrees : 0f;
        bool changed = false;
        ImGuiApi.SetNextItemWidth(Math.Max(width - 64f, 60f));
        float typed = deg;
        if (ImGuiApi.InputFloat($"##brg{node.Id.Value}", ref typed, 0f, 0f, "%.1f°"))
        {
            deg = BearingDial.Wrap(typed);
            changed = true;
        }
        ImGuiApi.SameLine();
        const float radius = 22f;
        var origin = ImGuiApi.GetCursorScreenPos();
        var centre = new System.Numerics.Vector2(origin.X + radius, origin.Y + radius);
        ImGuiApi.InvisibleButton($"##dial{node.Id.Value}", new System.Numerics.Vector2(radius * 2f, radius * 2f));
        if (ImGuiApi.IsItemActive())
        {
            float dragged = BearingDial.FromDrag(centre, ImGuiApi.GetMousePos());
            if (MathF.Abs(dragged - BearingDial.Wrap(deg)) > 0.05f) { deg = MathF.Round(dragged); changed = true; }
        }
        var dl = ImGuiApi.GetWindowDrawList();
        uint rim = ImGuiApi.GetColorU32(ImGuiNET.ImGuiCol.TextDisabled), needle = ImGuiApi.GetColorU32(ImGuiNET.ImGuiCol.PlotHistogramHovered);
        dl.AddCircle(centre, radius - 1f, rim, 24);
        dl.AddText(new System.Numerics.Vector2(centre.X - 3f, origin.Y - 1f), rim, "N");
        dl.AddLine(centre, BearingDial.NeedleEnd(centre, radius - 3f, deg), needle, 2f);
        if (changed) node.Binding?.SetBoxed(new Bearing(deg));
        return changed;
    }

    /// <summary>F7 — the muted read-out under a <see cref="WorldRotation"/> editor (<c>HPR [090.0, 0.0, 0.0] …</c>), through the ONE <see cref="OrientationText"/>.</summary>
    public static string RotationReadout(object? value)
        => value is WorldRotation r ? OrientationText.Format(r) : value is System.Numerics.Quaternion q ? OrientationText.Format(q) : "";

    // ── F9a: RefKey — the entity's authored name, unique per scope ─────────────────────────────────────────────────────────

    /// <summary>
    /// F9a — why <paramref name="text"/> cannot be this entity's key (red under the box), or <c>null</c> when it can. A key equal to the <paramref name="current"/> one is fine; empty clears
    /// the key; a key over 31 UTF-8 bytes is refused; a key another entity already carries is refused with that entity's name (SR-126: loud, and NOT applied).
    /// </summary>
    public static string? RefKeyProblem(string text, RefKey current, LiveRefScope? refs)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (text.TrimStart('@') == current.Value.ToString()) return null;
        RefSpec spec;
        try { spec = RefSpec.ByKey(text); }
        catch (ArgumentException e) { return e.Message.Split('\n')[0]; }
        if (refs == null) return null;
        string key = spec.KeyText;
        if (refs.HasDuplicate(key)) return $"key '{key}' is already used by more than one entity";
        return refs.TryResolve(spec, out var other) ? $"key '{key}' is already used by {refs.NameOf(other)}" : null;
    }

    /// <summary>F9a — the <see cref="RefKey"/> a text writes, or <c>false</c> (with the problem) when it must not be applied.</summary>
    public static bool TryApplyRefKey(string text, RefKey current, LiveRefScope? refs, out RefKey applied, out string? problem)
    {
        problem = RefKeyProblem(text, current, refs);
        applied = current;
        if (problem != null) return false;
        applied = string.IsNullOrEmpty(text) ? default : new RefKey(text.TrimStart('@'));
        return true;
    }

    private static bool DrawRefKey(EditNode node, float width, IMapPickContext? ctx, State st)
    {
        var current = node.Binding?.GetBoxed() is RefKey k ? k : default;
        string path = node.JsonPath;
        string text = st.KeyText.TryGetValue(path, out var t) ? t : current.Value.ToString();
        ImGuiApi.SetNextItemWidth(Math.Max(width - 8f, 80f));
        bool changed = false;
        if (ImGuiApi.InputText($"##refkey{node.Id.Value}", ref text, 64))
        {
            st.KeyText[path] = text;
            if (TryApplyRefKey(text, current, ctx?.References, out var applied, out _)) { node.Binding?.SetBoxed(applied); changed = true; }
        }
        if (RefKeyProblem(text, current, ctx?.References) is { } problem)
            ImGuiApi.TextColored(new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f), problem);   // refused: the box shows what was typed, the entity keeps its key
        return changed;
    }

    // ═══ drawing ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Draws the whole value cell of one reference field (the form switch, the value, the pick buttons); <c>true</c> when it wrote the binding.</summary>
    /// <param name="reserveRight">Pixels to leave free at the right (a delete button).</param>
    public static bool Draw(EditNode node, float reserveRight, IMapPickContext? ctx, State state)
    {
        float width = Math.Max(ImGuiApi.GetContentRegionAvail().X - reserveRight, 60f);
        if (node.ClrType == typeof(EntityLink)) { bool c = DrawEntityLink(node, width, ctx, state); DrawShowOnMap(node, ctx); return c; }
        if (node.ClrType == typeof(PointLink)) { bool c = DrawPointLink(node, width, ctx, state); DrawShowOnMap(node, ctx); return c; }
        if (node.ClrType == typeof(Bearing)) return DrawBearing(node, width);
        if (node.ClrType == typeof(RefKey)) return DrawRefKey(node, width, ctx, state);
        bool changed = node.ClrType == typeof(AreaLink) ? DrawAreaLink(node, width, ctx, state)
                     : node.ClrType == typeof(EntityRef) ? DrawEntityRef(node, width, ctx)
                     : DrawWorldPoint(node, width, ctx);
        DrawShowOnMap(node, ctx);
        return changed;
    }

    private static string[]? Presets(EditNode node)
        => node.Metadata.CustomAttributes.OfType<Fdp.Toolkit.Behavior.Attributes.MapPickableEntityAttribute>().FirstOrDefault() is { FilterPresets.Length: > 0 } a
            ? a.FilterPresets : null;

    // ── EntityRef (internal, unchanged behaviour) ────────────────────────────────────────────────────────────────────────────

    private static bool DrawEntityRef(EditNode node, float width, IMapPickContext? ctx)
    {
        bool changed = false;
        float input = width - (ctx != null ? (ctx.CanListEntities ? 140f : 90f) : 0f);
        ImGuiApi.SetNextItemWidth(Math.Max(input, 60f));
        string text = node.Binding?.GetBoxed() is EntityRef r ? r.NetworkId.ToString() : "0";   // the network id, typed in or set by the picker; 0 = none
        if (ImGuiApi.InputText("##v", ref text, 64) && int.TryParse(text, out int id))
        {
            node.Binding?.SetBoxed(new EntityRef(id));
            changed = true;
        }
        if (ctx != null && DrawEntityPickButtons(node, ctx, out var picked))
        {
            node.Binding?.SetBoxed(picked);
            changed = true;
        }
        return changed;
    }

    // ── WorldPoint (internal, unchanged behaviour) ───────────────────────────────────────────────────────────────────────────

    private static bool DrawWorldPoint(EditNode node, float width, IMapPickContext? ctx)
    {
        bool changed = false;
        ImGuiApi.SetNextItemWidth(Math.Max(width - (ctx != null ? 90f : 0f), 60f));
        var local = node.Binding?.GetBoxed() is WorldPoint wp ? (Vector3)wp : default;
        if (DrawXyz(ref local, Math.Max(width - (ctx != null ? 90f : 0f), 60f)))
        {
            node.Binding?.SetBoxed((WorldPoint)local);
            changed = true;
        }
        if (ctx != null && DrawLocationPickButton(node, ctx, out var location))
        {
            node.Binding?.SetBoxed(location.Local);   // a picked point writes the WorldPoint itself
            changed = true;
        }
        return changed;
    }

    private static bool DrawXyz(ref Vector3 local, float width)
    {
        // ⭐ S0 item 5 (SR-135) — a place in LOCAL METRES (x, y, z), typed in or set by the map picker; typed coordinates are a literal (no authored reference).
        float third = (width - 8f) / 3f;
        ImGuiApi.SetNextItemWidth(third);
        bool cx = ImGuiApi.InputFloat("##x", ref local.X, 0, 0, "%.2f");
        ImGuiApi.SameLine(0f, 4f);
        ImGuiApi.SetNextItemWidth(third);
        bool cy = ImGuiApi.InputFloat("##y", ref local.Y, 0, 0, "%.2f");
        ImGuiApi.SameLine(0f, 4f);
        ImGuiApi.SetNextItemWidth(third);
        bool cz = ImGuiApi.InputFloat("##z", ref local.Z, 0, 0, "%.2f");
        return cx || cy || cz;
    }

    // ── the pick buttons (shared) ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Pick Entity / List… and the consumption of a finished pick; <c>true</c> when an entity was picked this frame.</summary>
    private static bool DrawEntityPickButtons(EditNode node, IMapPickContext ctx, out EntityRef picked)
    {
        ImGuiApi.SameLine();
        if (ctx.IsPickPendingFor(node.JsonPath))
            ImGuiApi.TextColored(new Vector4(1f, 1f, 0f, 1f), "[Picking...]");
        else
        {
            if (ImGuiApi.Button($"Pick Entity##{node.Id.Value}"))
                ctx.RequestEntityPick(node.JsonPath, Presets(node));
            // ⭐ CE-1059 — or choose it from a searchable list, narrowed by the same presets; it comes back through the same TryConsumeEntityPick.
            if (ctx.CanListEntities)
            {
                ImGuiApi.SameLine();
                if (ImGuiApi.Button($"List…##{node.Id.Value}"))
                    ctx.RequestEntityListPick(node.JsonPath, Presets(node));
            }
        }
        return ctx.TryConsumeEntityPick(node.JsonPath, out picked);
    }

    private static bool DrawLocationPickButton(EditNode node, IMapPickContext ctx, out PointLink location)
    {
        ImGuiApi.SameLine();
        if (ctx.IsPickPendingFor(node.JsonPath))
            ImGuiApi.TextColored(new Vector4(1f, 1f, 0f, 1f), "[Picking...]");
        else if (ImGuiApi.Button($"Pick Map##{node.Id.Value}"))
            ctx.RequestLocationPick(node.JsonPath);
        return ctx.TryConsumeLocationPick(node.JsonPath, out location);
    }

    // ── EntityLink (F3 / F4) ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static bool DrawEntityLink(EditNode node, float width, IMapPickContext? ctx, State st)
    {
        var link = node.Binding?.GetBoxed() is EntityLink l ? l : EntityLink.None;
        bool changed = DrawEntityLinkCore(node, ref link, width, ctx, st);
        if (changed) node.Binding?.SetBoxed(link);
        return changed;
    }

    /// <summary>
    /// The F4 controls over an <see cref="EntityLink"/> value: the <c>[Exact | Key]</c> switch, then the entity (name, Pick, List) or the key box.
    /// Shared by the EntityLink leaf and by the PointLink leaf's Entity form (which is why it takes the value by <c>ref</c>).
    /// </summary>
    private static bool DrawEntityLinkCore(EditNode node, ref EntityLink link, float width, IMapPickContext? ctx, State st)
    {
        string path = node.JsonPath;
        var id = node.Id.Value;
        var refs = ctx?.References;
        bool changed = false;
        bool keyForm = st.KeyForm.TryGetValue(path, out var o) ? o : link.Spec.Form == RefForm.Key;

        ImGuiApi.PushID(path);
        if (!keyForm)
        {
            // ⭐ U-8 (DESIGN_Condition_Editor §8.2 ④, condition-expert-visual.svg) — THE THING BY NAME: one chip — its kind's glyph in colour · "Objective: Hold the
            // bridge" · ▾ — whose menu picks on the map, from a list, by key, or clears; then the map-pick glyph. ⛔ No Exact/Key radio, never "#10" when a name exists.
            if (DrawChip("##chip", ChipLabel(link, refs), ChipIcon(link, refs), Math.Max(width - 2f * ImGuiApi.GetFrameHeight() - 8f, 80f)))
                ImGuiApi.OpenPopup("thing");
            if (ImGuiApi.BeginPopup("thing"))
            {
                if (ctx != null && ImGuiApi.Selectable("Pick on the map")) ctx.RequestEntityPick(path, Presets(node));
                if (ctx is { CanListEntities: true } && ImGuiApi.Selectable("Choose from a list…")) ctx.RequestEntityListPick(path, Presets(node));
                if (ImGuiApi.Selectable("By key (@name)…"))
                {
                    st.KeyForm[path] = true;
                    var next = SwitchToKey(link, refs, out var text);
                    st.KeyText[path] = text;
                    if (next != link) { link = next; changed = true; }
                }
                if (!link.Ref.IsNone && ImGuiApi.Selectable("Clear")) { link = EntityLink.None; changed = true; }
                ImGuiApi.EndPopup();
            }
            if (ctx != null)
            {
                ImGuiApi.SameLine(0f, 4f);
                if (ctx.IsPickPendingFor(path)) ImGuiApi.TextColored(new Vector4(1f, 1f, 0f, 1f), "[Picking...]");
                else if (NodeEditor.Core.Widgets.GlyphWidgets.GlyphButton("##mappick", NodeEditor.Core.Widgets.Icons.Get("form/map-pick"), "Pick it on the map"))
                    ctx.RequestEntityPick(path, Presets(node));
                if (ctx.TryConsumeEntityPick(path, out var picked)) { link = PickToLink(picked, refs); changed = true; }
            }
            if (NeedsStableIdWarning(link, refs))
            {
                ImGuiApi.SameLine();
                ImGuiApi.TextDisabled(NoStableId);
            }
        }
        else
        {
            // ── Key: a text box completing from Keys(), unresolved shown red. (SR-133: while a MACRO is edited a pick writes Key — after S3a.) ──
            // ⭐ U-8 — the way back to ONE THING (the old Exact radio): the pick glyph ahead of the key box.
            if (NodeEditor.Core.Widgets.GlyphWidgets.GlyphButton("##bything", NodeEditor.Core.Widgets.Icons.Get("form/pick"), "Link one thing instead of a key"))
            {
                st.KeyForm[path] = false;
                link = SwitchToExact(link, refs);
                changed = true;
            }
            ImGuiApi.SameLine(0f, 4f);
            string text = st.KeyText.TryGetValue(path, out var t) ? t : link.Spec.KeyText;
            ImGuiApi.SetNextItemWidth(Math.Max(width - 190f, 80f));
            if (ImGuiApi.InputText($"##key{id}", ref text, 64))
            {
                st.KeyText[path] = text;
                if (TryKeyToLink(text, refs, out var written, out _) && written != link) { link = written; changed = true; }
            }
            ImGuiApi.SameLine(0f, 4f);
            if (ImGuiApi.SmallButton($"v##kc{id}")) ImGuiApi.OpenPopup($"keys##{id}");
            if (ImGuiApi.BeginPopup($"keys##{id}"))
            {
                foreach (var k in refs?.Keys() ?? Array.Empty<string>())
                    if (k.StartsWith(text.TrimStart('@'), StringComparison.OrdinalIgnoreCase) && ImGuiApi.Selectable(k))
                    {
                        st.KeyText[path] = k;
                        if (TryKeyToLink(k, refs, out var chosen, out _)) { link = chosen; changed = true; }
                    }
                ImGuiApi.EndPopup();
            }
            if (KeyProblem(text, refs) is { } problem)
            {
                ImGuiApi.SameLine();
                ImGuiApi.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), problem);   // TextColored takes the string as a plain argument (never a format)
            }
        }
        ImGuiApi.PopID();
        return changed;
    }

    // ── ⭐ U-8 the thing chip ────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ U-8 — the chip's text: <c>Objective: Hold the bridge</c> (the scope's kind + name), the name alone without a kind, <c>#id</c> only when the
    /// thing has no name, <c>(none)</c> for none. ⛔ Never <c>Name (#id)</c>, never <c>Key #10</c>. Cached by the scope ⇒ no allocation per frame.</summary>
    public static string ChipLabel(EntityLink link, LiveRefScope? refs)
        => link.Ref.IsNone ? "(none)" : refs == null ? "#" + link.Ref.NetworkId : refs.ChipLabelOf(link.Ref);

    /// <summary>⭐ U-8 — the chip's glyph: the thing's kind (<c>thing/objective</c> · <c>thing/trigger</c> · <c>thing/entity</c>), null when none is known.</summary>
    public static NodeEditor.Core.Widgets.IconGlyph? ChipIcon(EntityLink link, LiveRefScope? refs)
        => !link.Ref.IsNone && refs != null && refs.TryKindOf(link.Ref, out var k) && k.IconKey.Length > 0 && NodeEditor.Core.Widgets.Icons.TryGet(k.IconKey, out var g) ? g : null;

    /// <summary>A frame-coloured chip — glyph · text · ▾ — sized by its text (capped at <paramref name="maxWidth"/>); true when clicked.</summary>
    private static bool DrawChip(string id, string label, NodeEditor.Core.Widgets.IconGlyph? icon, float maxWidth)
    {
        var style = ImGuiApi.GetStyle();
        float h = ImGuiApi.GetFrameHeight(), pad = style.FramePadding.X, arrow = h * 0.55f;
        float glyphW = icon is { } ig ? ImGuiApi.CalcTextSize(ig.Glyph).X + 5f : 0f;
        float w = MathF.Min(maxWidth, pad + glyphW + ImGuiApi.CalcTextSize(label).X + 6f + arrow + pad);
        var pos = ImGuiApi.GetCursorScreenPos();
        bool clicked = ImGuiApi.InvisibleButton(id, new Vector2(w, h));
        bool hovered = ImGuiApi.IsItemHovered();
        var dl = ImGuiApi.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(w, h), ImGuiApi.GetColorU32(hovered ? ImGuiNET.ImGuiCol.FrameBgHovered : ImGuiNET.ImGuiCol.FrameBg), style.FrameRounding);
        float ty = pos.Y + (h - ImGuiApi.GetTextLineHeight()) * 0.5f, x = pos.X + pad;
        if (icon is { } g) { dl.AddText(new Vector2(x, ty), ImGuiApi.GetColorU32(g.Color), g.Glyph); x += glyphW; }
        dl.PushClipRect(new Vector2(x, pos.Y), new Vector2(pos.X + w - arrow - pad, pos.Y + h), true);
        dl.AddText(new Vector2(x, ty), ImGuiApi.GetColorU32(ImGuiNET.ImGuiCol.Text), label);
        dl.PopClipRect();
        float ax = pos.X + w - pad - arrow * 0.5f, ay = pos.Y + h * 0.5f, r = arrow * 0.32f;
        dl.AddTriangleFilled(new Vector2(ax - r, ay - r * 0.6f), new Vector2(ax + r, ay - r * 0.6f), new Vector2(ax, ay + r * 0.6f), ImGuiApi.GetColorU32(ImGuiNET.ImGuiCol.TextDisabled));
        return clicked;
    }

    // ── PointLink (F5) ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private static bool DrawPointLink(EditNode node, float width, IMapPickContext? ctx, State st)
    {
        var point = node.Binding?.GetBoxed() is PointLink p ? p : default;
        string path = node.JsonPath;
        var id = node.Id.Value;
        var refs = ctx?.References;
        bool entityForm = st.PointEntityForm.TryGetValue(path, out var o) ? o : !point.Spec.IsNone;
        bool changed = false;

        if (ImGuiApi.RadioButton($"Coordinates##pc{id}", !entityForm)) st.PointEntityForm[path] = entityForm = false;
        ImGuiApi.SameLine(0f, 4f);
        if (ImGuiApi.RadioButton($"Entity##pe{id}", entityForm)) st.PointEntityForm[path] = entityForm = true;
        ImGuiApi.SameLine();

        if (!entityForm)
        {
            // today's form: x, y, z + Pick Map — typed coordinates are a literal (no authored reference)
            var local = (Vector3)point.Local;
            if (DrawXyz(ref local, Math.Max(width - 90f - 190f, 120f)))
            {
                point = new PointLink(local);
                changed = true;
            }
            if (ctx != null && DrawLocationPickButton(node, ctx, out var location))
            {
                point = location;
                changed = true;
            }
        }
        else
        {
            // F4's controls; the answer `Local` is set from the entity's position when the reference is assigned (a snapshot — SR-130)
            EntityRef shown = point.Spec.IsNone ? EntityRef.None : (refs != null && refs.TryResolve(point.Spec, out var r) ? r : EntityRef.None);
            var link = new EntityLink(shown, point.Spec);
            var before = link;
            if (DrawEntityLinkCoreForPoint(node, ref link, width, ctx, st, out var pickedEntity))
            {
                if (pickedEntity is { } pe)
                {
                    point = PointFromPick(pe, point.Local, refs, out bool stable);
                    if (stable) st.Notice.Remove(path); else st.Notice[path] = "entity has no stable id — its position was copied as coordinates";
                }
                else
                    point = PointFromSpec(link.Spec, link.Ref, point.Local, refs);
                changed = point != (node.Binding?.GetBoxed() is PointLink cur ? cur : default);
            }
            if (st.Notice.TryGetValue(path, out var note))
            {
                ImGuiApi.SameLine();
                ImGuiApi.TextDisabled(note);
            }
        }

        if (changed) node.Binding?.SetBoxed(point);
        return changed;
    }

    /// <summary>The Entity form's controls. A pick is reported through <paramref name="picked"/> (so the caller can fall back to coordinates for an
    /// entity with no GUID) rather than written into the link.</summary>
    private static bool DrawEntityLinkCoreForPoint(EditNode node, ref EntityLink link, float width, IMapPickContext? ctx, State st, out EntityRef? picked)
    {
        picked = null;
        var shadow = ctx == null ? null : new PickSink(ctx);
        bool changed = DrawEntityLinkCore(node, ref link, width, shadow, st);
        if (shadow?.Picked is { } p) { picked = p; return true; }
        return changed;
    }

    /// <summary>Wraps a pick context so a consumed entity pick is remembered instead of lost (the PointLink Entity form decides what to write).</summary>
    private sealed class PickSink : IMapPickContext
    {
        private readonly IMapPickContext? _inner;
        public EntityRef? Picked;
        public PickSink(IMapPickContext? inner) => _inner = inner;
        public bool IsPickPendingFor(string p) => _inner?.IsPickPendingFor(p) ?? false;
        public void RequestEntityPick(string p, string[]? f) => _inner?.RequestEntityPick(p, f);
        public void RequestLocationPick(string p) => _inner?.RequestLocationPick(p);
        public bool TryConsumeEntityPick(string p, out EntityRef picked)
        {
            picked = default;
            if (_inner == null || !_inner.TryConsumeEntityPick(p, out picked)) return false;
            Picked = picked;
            picked = EntityRef.None;   // the core sees no pick; the caller reads Picked
            return false;
        }
        public bool TryConsumeLocationPick(string p, out PointLink l) { l = default; return false; }
        public bool CanListEntities => _inner?.CanListEntities ?? false;
        public void RequestEntityListPick(string p, string[]? f) => _inner?.RequestEntityListPick(p, f);
        public LiveRefScope? References => _inner?.References;
    }
}
