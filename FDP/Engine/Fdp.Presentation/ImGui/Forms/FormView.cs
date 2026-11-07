using System;
using System.Collections.Generic;
using System.Globalization;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Replication.Patching;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Fdp.Presentation.Forms;

/// <summary>
/// ⭐ U-12 / D1 (docs/UX/DESIGN_Details_Forms.md §2.1–§2.4, §6) — THE shared form view: draws a <see cref="FormDecl"/> for a selection on <see cref="FormGrid"/>
/// through D0's <see cref="FormRow"/> (label · control · status slot). ⭐ A value at rest is a CACHED string, rebuilt only when the world's value changes
/// (0 B per steady frame — CE-4285); numbers edit in place with the cached format, so no StructEdit session is opened for a scalar.
/// ⭐ Multi-select: the rows common to the selection, a differing value shows <i>mixed</i>, setting it writes every entity (one <c>Submit</c> each).
/// ⭐ Every write is <see cref="Edits.Submit"/> (<see cref="EditOp.Write(Entity, EntityAttributeChange, string)"/>): the form never decides local or remote; the outcome
/// marks the row (⏳ pending, ✗ refused with the reason). Read-only (a recording, a seat without the right) = the whole form disabled, nothing written.
/// One instance per host panel (it keeps the panel's cache and the row being edited).
/// </summary>
public sealed class FormView
{
    /// <summary>The ONE write seam (default <see cref="Edits.Submit"/>); a rail captures the ops here.</summary>
    public Func<EntityRepository, EditOp, EditTicket> Submit { get; set; } = Edits.Submit;

    /// <summary>The panel's Expert switch (§2.3, D7): the expert rows open by default. Saved per panel by the host.</summary>
    public bool ExpertOpen { get; set; }

    /// <summary>⭐ U-23 / D4 — THE pick context a list's add action picks through (map pick, and a list where the context offers one); null = no add.</summary>
    public IMapPickContext? Pick { get; set; }

    private sealed class FieldState
    {
        public bool Has, Mixed, Known;
        public double Number;
        public FixedString64 Text;
        public string Shown = "";
        public string Id = "";
        public string? Problem;
        public readonly List<Guid> Tickets = new();
        public string? Refusal;
    }

    private FormDecl? _decl;
    private FieldState[] _state = Array.Empty<FieldState>();
    private int _editing = -1;
    private double _editNumber;
    private string _editText = "";
    private int _selectionStamp;
    private string _expertId = "", _expertGridId = "";
    private string[] _listIds = Array.Empty<string>();
    private string?[] _listProblem = Array.Empty<string?>();
    private readonly List<FormListItem> _items = new();
    private readonly Dictionary<long, (FixedString64 Who, string Shown)> _who = new();   // a row's name, built once per change (0 B steady)
    private readonly List<string> _listRows = new();

    /// <summary>Draws <paramref name="decl"/> for <paramref name="selection"/>; <paramref name="editable"/> false ⇒ read-only (the run state / the seat decides — ONE function, §2.4).</summary>
    public void Draw(FormDecl decl, EntityRepository world, IReadOnlyList<Entity> selection, bool editable)
    {
        if (!ReferenceEquals(decl, _decl)) Bind(decl);
        int stamp = Stamp(selection);
        if (stamp != _selectionStamp) { _selectionStamp = stamp; _editing = -1; foreach (var s in _state) { s.Known = false; s.Tickets.Clear(); s.Refusal = null; s.Problem = null; } }
        Refresh(world, selection);

        if (!editable) ImGui.BeginDisabled();
        if (decl.Fields.Count > 0 && FormGrid.Begin(decl.Id, decl.Labels, statusSlot: true))
        {
            for (int i = 0; i < decl.Fields.Count; i++)
                if (!decl.Fields[i].Expert && _state[i].Has) DrawField(i, world, selection, editable);
            FormGrid.End();
        }
        bool anyExpert = false;
        for (int i = 0; i < decl.Fields.Count; i++) anyExpert |= decl.Fields[i].Expert && _state[i].Has;
        if (anyExpert && SectionHeader.Draw(_expertId, "Details for experts", "form/info", ExpertOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
        {
            if (FormGrid.Begin(_expertGridId, decl.Labels, statusSlot: true))
            {
                for (int i = 0; i < decl.Fields.Count; i++)
                    if (decl.Fields[i].Expert && _state[i].Has) DrawField(i, world, selection, editable);
                FormGrid.End();
            }
        }
        _listRows.Clear();
        if (selection.Count == 1)
            for (int li = 0; li < decl.Lists.Count; li++) DrawList(li, decl.Lists[li], world, selection[0], editable);
        if (!editable) ImGui.EndDisabled();
    }

    // ── ⭐ U-23 / D4 — a list with actions (the seats): rows, a per-row action, an add through the one pick context ───────────────────────────

    private void DrawList(int li, FormList list, EntityRepository world, Entity e, bool editable)
    {
        string id = _listIds[li];
        if (editable && Pick != null && list.Add != null && Pick.TryConsumeEntityPick(id, out var picked) && picked.TryResolve(world, out var pickedEntity))
            _listProblem[li] = list.Add(world, e, pickedEntity);
        _items.Clear();
        list.Fill(world, e, _items);
        ImGui.PushID(id);
        ImGui.Spacing();
        ImGui.TextDisabled(list.Label);
        for (int j = 0; j < _items.Count; j++)
        {
            var it = _items[j];
            string who = WhoOf(it);
            _listRows.Add(it.Role); _listRows.Add(who);
            ImGui.PushID(j);
            ImGui.TextUnformatted(it.Role);
            ImGui.SameLine(120f);
            ImGui.TextUnformatted(who);
            if (list.Remove != null && list.RemoveLabel != null)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton(list.RemoveLabel) && editable) { list.Remove(world, it); _listProblem[li] = null; }
            }
            ImGui.PopID();
        }
        if (list.Footer?.Invoke(world, e) is { } footer) ImGui.TextDisabled(footer);
        if (list.Add != null && list.AddLabel != null && Pick != null)
        {
            if (Pick.IsPickPendingFor(id)) ImGui.TextDisabled("pick on the map…");
            else
            {
                if (ImGui.Button(list.AddLabel)) { Pick.RequestEntityPick(id, null); _listProblem[li] = null; }
                if (Pick.CanListEntities) { ImGui.SameLine(); if (ImGui.Button("List…")) { Pick.RequestEntityListPick(id, null); _listProblem[li] = null; } }
            }
        }
        if (_listProblem[li] is { } problem) ImGui.TextColored(new System.Numerics.Vector4(0.9f, 0.4f, 0.35f, 1f), problem);
        ImGui.PopID();
    }

    private string WhoOf(in FormListItem it)
    {
        long key = ((long)it.Item.Index << 32) | (uint)it.Item.Generation;
        if (_who.TryGetValue(key, out var c) && c.Who.Equals(it.Who)) return c.Shown;
        string shown = it.Who.ToString();
        _who[key] = (it.Who, shown);
        return shown;
    }

    private void Bind(FormDecl decl)
    {
        _decl = decl;
        _expertId = decl.Id + "-expert"; _expertGridId = decl.Id + "-x";   // built once per form, never per frame
        _state = new FieldState[decl.Fields.Count];
        for (int i = 0; i < _state.Length; i++) _state[i] = new FieldState { Id = decl.Id + "." + i };
        _listIds = new string[decl.Lists.Count]; _listProblem = new string?[decl.Lists.Count];
        for (int i = 0; i < _listIds.Length; i++) _listIds[i] = decl.Id + ".list" + i;
        _editing = -1;
    }

    private static int Stamp(IReadOnlyList<Entity> sel)
    {
        unchecked
        {
            int h = sel.Count;
            for (int i = 0; i < sel.Count; i++) h = (h * 31 + sel[i].Index) * 31 + sel[i].Generation;   // the fields — ValueType.GetHashCode would box
            return h;
        }
    }

    // ── read the world: 0 B, the shown text rebuilt only on a change ─────────────────────────────────────────────────────────

    private void Refresh(EntityRepository world, IReadOnlyList<Entity> sel)
    {
        var fields = _decl!.Fields;
        for (int f = 0; f < fields.Count; f++)
        {
            var field = fields[f]; var s = _state[f];
            bool has = false, mixed = false; double num = 0; FixedString64 text = default;
            for (int i = 0; i < sel.Count; i++)
            {
                var e = sel[i];
                if (!world.IsAlive(e) || (field.Has != null && !field.Has(world, e))) { has = false; break; }   // a row common to ALL the selection only
                if (field.Kind == FieldKind.Text)
                {
                    var t = field.ReadText!(world, e);
                    if (!has) text = t; else if (!t.Equals(text)) mixed = true;
                }
                else
                {
                    double v = field.ReadNumber!(world, e);
                    if (!has) num = v; else if (v != num) mixed = true;
                }
                has = true;
            }
            s.Has = has && sel.Count > 0;
            if (!s.Has) continue;
            if (s.Known && s.Mixed == mixed && (field.Kind == FieldKind.Text ? s.Text.Equals(text) : s.Number == num)) continue;
            s.Known = true; s.Mixed = mixed; s.Number = num; s.Text = text;
            s.Shown = mixed ? "mixed" : Format(field, num, text);
        }
    }

    /// <summary>The value as people read it: <c>60 s</c> · <c>62 %</c> · the choice's word · <c>yes / no</c> · the text.</summary>
    public static string Format(FormField field, double number, in FixedString64 text) => field.Kind switch
    {
        FieldKind.Text => text.ToString(),
        FieldKind.Choice => field.Choices != null && (int)number >= 0 && (int)number < field.Choices.Count ? field.Choices[(int)number] : number.ToString(CultureInfo.InvariantCulture),
        FieldKind.Toggle => number != 0 ? "yes" : "no",
        _ => number.ToString("0.##", CultureInfo.InvariantCulture) + (field.Unit == null ? "" : " " + field.Unit),
    };

    // ── one row ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private void DrawField(int i, EntityRepository world, IReadOnlyList<Entity> sel, bool editable)
    {
        var field = _decl!.Fields[i]; var s = _state[i];
        var rowState = StateOf(s, world, field, editable);
        FormRow.Begin(s.Id, field.Label, in rowState);
        ImGui.SetNextItemWidth(-1f);
        bool ro = field.ReadOnly || !editable;
        if (ro) ImGui.BeginDisabled();
        switch (field.Kind)
        {
            case FieldKind.Number: DrawNumber(i, field, s, world, sel); break;
            case FieldKind.Text: DrawText(i, field, s, world, sel); break;
            case FieldKind.Choice:
                if (ImGui.BeginCombo("##c", s.Shown))
                {
                    var choices = field.Choices ?? Array.Empty<string>();
                    for (int c = 0; c < choices.Count; c++)
                        if (ImGui.Selectable(choices[c], !s.Mixed && (int)s.Number == c)) Commit(i, world, sel, c, null);
                    ImGui.EndCombo();
                }
                break;
            case FieldKind.Toggle:
                bool b = s.Number != 0;
                if (ImGui.Checkbox("##b", ref b)) Commit(i, world, sel, b ? 1 : 0, null);
                break;
        }
        if (ro) ImGui.EndDisabled();
        if (FormRow.End(in rowState) == FormMark.Refused) { s.Refusal = null; s.Tickets.Clear(); }
    }

    private void DrawNumber(int i, FormField field, FieldState s, EntityRepository world, IReadOnlyList<Entity> sel)
    {
        if (s.Mixed && _editing != i)
        {
            if (ImGui.Button("mixed##m", new System.Numerics.Vector2(-1f, 0f))) { _editing = i; _editNumber = s.Number; }
            return;
        }
        double v = _editing == i ? _editNumber : s.Number;
        ImGui.InputDouble("##n", ref v, 0, 0, NumberInput.FormatFor(field.Unit));
        if (ImGui.IsItemActivated()) { _editing = i; _editNumber = s.Number; }
        if (_editing == i)
        {
            _editNumber = v;
            s.Problem = field.RangeProblem(v);
            if (ImGui.IsItemDeactivatedAfterEdit() && s.Problem == null) Commit(i, world, sel, v, null);   // on Enter / leaving the field (§2.3) — ⛔ never per keystroke
            if (ImGui.IsItemDeactivated()) _editing = -1;
        }
    }

    private void DrawText(int i, FormField field, FieldState s, EntityRepository world, IReadOnlyList<Entity> sel)
    {
        string t = _editing == i ? _editText : s.Shown;
        ImGui.InputText("##t", ref t, 64);
        if (ImGui.IsItemActivated()) { _editing = i; _editText = s.Mixed ? "" : s.Shown; }
        if (_editing == i)
        {
            _editText = t;
            if (ImGui.IsItemDeactivatedAfterEdit()) Commit(i, world, sel, 0, t);
            if (ImGui.IsItemDeactivated()) _editing = -1;
        }
    }

    private static FormRowState StateOf(FieldState s, EntityRepository world, FormField field, bool editable)
    {
        if (!editable) return default;   // a recording / a seat: grey only — no marks, no tooltips (§2.4, R-4150)
        if (s.Problem != null) return new FormRowState(Invalid: true, InvalidReason: s.Problem);
        if (s.Refusal != null) return new FormRowState(Refused: true, RefusedReason: s.Refusal);
        if (s.Tickets.Count > 0)
        {
            var log = EditOutcomeLog.Of(world);
            bool pending = false;
            for (int k = s.Tickets.Count - 1; k >= 0; k--)
            {
                if (!log.TryGet(s.Tickets[k], out var o) || o.Status is EditStatus.None or EditStatus.Pending) { pending = true; continue; }
                if (o.Status is EditStatus.Refused or EditStatus.Conflict or EditStatus.Lost) s.Refusal = o.Reason.Length > 0 ? o.Reason : o.Status.ToString();
                s.Tickets.RemoveAt(k);
            }
            if (s.Refusal != null) return new FormRowState(Refused: true, RefusedReason: s.Refusal);
            if (pending) return new FormRowState(Pending: true, PendingTip: "sent to the owner — waiting for its answer");
        }
        return default;
    }

    /// <summary>Writes one value to every selected entity (one <c>Submit</c> each); the tickets mark the row until the owners answer.</summary>
    private void Commit(int i, EntityRepository world, IReadOnlyList<Entity> sel, double number, string? text)
    {
        var field = _decl!.Fields[i]; var s = _state[i];
        if (field.Apply != null)
        {
            // ⭐ U-23 part 5 — a field that writes through a DOMAIN op (the Playable toggle: an owner-side add / remove), never an attribute
            s.Refusal = null;
            for (int k = 0; k < sel.Count; k++) if (world.IsAlive(sel[k]) && field.Apply(world, sel[k], number, text) is { } why) s.Refusal = why;
            s.Known = false;
            return;
        }
        if (field.ChangeFor(number, text) is not { } change) return;
        s.Refusal = null;
        string label = "Set " + field.Label.ToLowerInvariant();
        for (int k = 0; k < sel.Count; k++)
        {
            if (!world.IsAlive(sel[k])) continue;
            var ticket = Submit(world, EditOp.Write(sel[k], change, label));
            if (ticket.Route == Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused)
            {
                s.Refusal = EditOutcomeLog.Of(world).TryGet(ticket.RequestId, out var o) && o.Reason.Length > 0 ? o.Reason : "refused";
                continue;
            }
            if (ticket.Route != Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Direct) s.Tickets.Add(ticket.RequestId);
        }
        s.Known = false;   // re-read and re-format next frame
    }

    // ── rails ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>What each drawn row shows, as text (rails, the panel probe): <c>label = value</c>.</summary>
    public IReadOnlyList<string> Rows
    {
        get
        {
            var l = new List<string>();
            if (_decl == null) return l;
            for (int i = 0; i < _state.Length; i++) if (_state[i].Has) l.Add($"{_decl.Fields[i].Label} = {_state[i].Shown}");
            return l;
        }
    }

    /// <summary>⭐ U-23 / D4 — the last drawn lists' rows, role then who (rails).</summary>
    public IReadOnlyList<string> ListRows => _listRows.ToArray();

    /// <summary>⭐ U-23 / D4 — the reason the last add was refused, per list (rails).</summary>
    public string? ListProblem(int list) => list < _listProblem.Length ? _listProblem[list] : null;

    /// <summary>Commits <paramref name="number"/> / <paramref name="text"/> to field <paramref name="field"/> of the last drawn selection, as a click would (rails).</summary>
    public void CommitForTest(int field, EntityRepository world, IReadOnlyList<Entity> selection, double number, string? text = null)
        => Commit(field, world, selection, number, text);

    /// <summary>The same, binding <paramref name="decl"/> first (a rail with no frame drawn).</summary>
    public void CommitForTest(FormDecl decl, int field, EntityRepository world, IReadOnlyList<Entity> selection, double number, string? text = null)
    {
        if (!ReferenceEquals(decl, _decl)) Bind(decl);
        Commit(field, world, selection, number, text);
    }
}
