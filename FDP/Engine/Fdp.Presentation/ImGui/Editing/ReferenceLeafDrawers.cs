using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using StructEdit.Core;

namespace Fdp.Presentation.Editing;

using ImGuiApi = ImGuiNET.ImGui;

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
        => type == typeof(EntityLink) || type == typeof(PointLink) || type == typeof(EntityRef) || type == typeof(WorldPoint);

    /// <summary>The words shown beside an entity written as a bare id.</summary>
    public const string NoStableId = "no stable id — will not survive a save";

    /// <summary>UI state the drawer keeps per field path: the form a person switched to, the key text being typed, a note.</summary>
    public sealed class State
    {
        internal readonly Dictionary<string, bool> KeyForm = new();
        internal readonly Dictionary<string, string> KeyText = new();
        internal readonly Dictionary<string, bool> PointEntityForm = new();
        internal readonly Dictionary<string, string> Notice = new();
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

    // ═══ drawing ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Draws the whole value cell of one reference field (the form switch, the value, the pick buttons); <c>true</c> when it wrote the binding.</summary>
    /// <param name="reserveRight">Pixels to leave free at the right (a delete button).</param>
    public static bool Draw(EditNode node, float reserveRight, IMapPickContext? ctx, State state)
    {
        float width = Math.Max(ImGuiApi.GetContentRegionAvail().X - reserveRight, 60f);
        if (node.ClrType == typeof(EntityLink)) return DrawEntityLink(node, width, ctx, state);
        if (node.ClrType == typeof(PointLink)) return DrawPointLink(node, width, ctx, state);
        if (node.ClrType == typeof(EntityRef)) return DrawEntityRef(node, width, ctx);
        return DrawWorldPoint(node, width, ctx);
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

        // ── the two-way switch ────────────────────────────────────────────────────────────────────────────────────────────
        if (ImGuiApi.RadioButton($"Exact##ex{id}", !keyForm) && keyForm)
        {
            st.KeyForm[path] = false;
            link = SwitchToExact(link, refs);
            changed = true; keyForm = false;
        }
        ImGuiApi.SameLine(0f, 4f);
        if (ImGuiApi.RadioButton($"Key##ky{id}", keyForm) && !keyForm)
        {
            st.KeyForm[path] = true;
            var next = SwitchToKey(link, refs, out var text);
            st.KeyText[path] = text;
            if (next != link) { link = next; changed = true; }
            keyForm = true;
        }
        ImGuiApi.SameLine();

        if (!keyForm)
        {
            // ── Exact: "Name (#id)" + Pick / List ─────────────────────────────────────────────────────────────────────────
            string shown = link.Ref.IsNone ? "(none)"
                : refs == null ? "#" + link.Ref.NetworkId
                : refs.NameOf(link.Ref) is var n && n.StartsWith('#') ? n : $"{n} (#{link.Ref.NetworkId})";
            ImGuiApi.TextUnformatted(shown);
            if (ctx != null && DrawEntityPickButtons(node, ctx, out var picked))
            {
                link = PickToLink(picked, refs);
                changed = true;
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
        return changed;
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
