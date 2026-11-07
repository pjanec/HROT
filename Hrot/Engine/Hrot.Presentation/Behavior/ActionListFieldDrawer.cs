using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.ScenarioRules.Actions;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using StructEdit.Core;

namespace Hrot.Presentation.Behavior;

/// <summary>
/// ⭐⭐ CE-4220 (rules view R5b, RE-4; docs/DESIGN_Rules_View_UI.md §7.3) — the do list's edits, PURE: every operation returns a NEW list (an <see cref="ActionStep"/> is immutable), and the list serialises through
/// <see cref="ActionStep.ToJson"/> — the very string <see cref="ActionList.Get"/> parses and <see cref="ActionList.Validate"/> checks, so what the editor writes is what the runtime reads.
/// </summary>
public static class ActionListEdit
{
    /// <summary>The name a step carries for <paramref name="entry"/>: the short name, or the full name when another action shares it (a short name that is ambiguous is refused by the catalogue).</summary>
    public static string ActName(ActionEntry entry)
        => ActionCatalogue.Ambiguities.ContainsKey(entry.ShortName) ? entry.Fqn : entry.ShortName;

    /// <summary>The catalogue entry of <paramref name="act"/>, or null (unknown, or an ambiguous short name).</summary>
    public static ActionEntry? EntryOf(string act)
    {
        try { return ActionCatalogue.TryGet(act, out var e) ? e : null; }
        catch (InvalidOperationException) { return null; }
    }

    public static List<ActionStep> Added(IReadOnlyList<ActionStep>? list, ActionEntry entry)
        => new(list ?? Array.Empty<ActionStep>()) { new ActionStep { Act = ActName(entry), Args = new JsonObject() } };

    public static List<ActionStep> Removed(IReadOnlyList<ActionStep> list, int index)
    {
        var l = new List<ActionStep>(list);
        if ((uint)index < (uint)l.Count) l.RemoveAt(index);
        return l;
    }

    /// <summary>The list with the step at <paramref name="index"/> moved by <paramref name="delta"/> (±1); unchanged at the ends.</summary>
    public static List<ActionStep> Moved(IReadOnlyList<ActionStep> list, int index, int delta)
    {
        var l = new List<ActionStep>(list);
        int to = index + delta;
        if ((uint)index < (uint)l.Count && (uint)to < (uint)l.Count) { var s = l[index]; l.RemoveAt(index); l.Insert(to, s); }
        return l;
    }

    /// <summary>⭐ CE-4307 (RX5) — an <c>Order</c> step's ORDER KIND: its <c>behavior</c> arg as <c>Order.&lt;kind&gt;</c> (the receiver's class picks the behaviour),
    /// other args kept; null = the step's current kind, or null when it names a behaviour (Advanced) or nothing.</summary>
    public static string? OrderKindOf(ActionStep step)
    {
        if (step.Args?["behavior"] is not System.Text.Json.Nodes.JsonValue v || !v.TryGetValue<string>(out var b)) return null;
        string prefix = Hrot.Map.Definitions.Behavior.Orders.OrderAttribute.IntentPrefix;
        return b.StartsWith(prefix, StringComparison.Ordinal) ? b.Substring(prefix.Length) : null;
    }

    /// <summary>⭐ CE-4307 — the list with Order step <paramref name="index"/> set to order kind <paramref name="kind"/> (the other args kept).</summary>
    public static List<ActionStep> WithOrderKind(IReadOnlyList<ActionStep> list, int index, string kind)
    {
        var args = list[index].Args?.DeepClone() as JsonObject ?? new JsonObject();
        args["behavior"] = Hrot.Map.Definitions.Behavior.Orders.OrderAttribute.IntentPrefix + kind;
        return WithArgs(list, index, args);
    }

    /// <summary>Whether <paramref name="act"/> is the <c>Order</c> verb (short or full name).</summary>
    public static bool IsOrder(string act) => act == "Order" || act.EndsWith(".Order", StringComparison.Ordinal);

    /// <summary>The list with step <paramref name="index"/>'s args replaced (act and bindings kept).</summary>
    public static List<ActionStep> WithArgs(IReadOnlyList<ActionStep> list, int index, JsonObject args)
    {
        var l = new List<ActionStep>(list);
        if ((uint)index < (uint)l.Count) l[index] = new ActionStep { Act = l[index].Act, Args = args, Bind = l[index].Bind };
        return l;
    }

    /// <summary>
    /// The args after a form edit: the form's full serialisation (<paramref name="formJson"/>, the params type's own) laid over the step's original args — ⭐ a key the params type does not know (a nested <c>"params"</c>) is kept, never silently dropped.
    /// </summary>
    public static JsonObject MergeArgs(JsonObject? original, string formJson)
    {
        var result = original != null ? (JsonObject)original.DeepClone() : new JsonObject();
        if (JsonNode.Parse(formJson) is JsonObject form)
            foreach (var (key, value) in form)
            {
                string? existing = result.Select(kv => kv.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                result[existing ?? key] = value?.DeepClone();
            }
        return result;
    }

    /// <summary>The picker rows of the catalogue: ShortName, the full name as the detail; an action with a <see cref="ActionEntry.Refusal"/> is listed DISABLED and says why.</summary>
    public static IReadOnlyList<PickerEntry> PickerEntries()
        => ActionCatalogue.All.OrderBy(e => e.ShortName, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Fqn, StringComparer.Ordinal)
            .Select(e => new PickerEntry(e.Fqn, e.ShortName,
                e.Refusal == null ? e.Fqn : $"{e.Fqn}\n\nNot available as a step: {e.Refusal}",
                null, new[] { e.Fqn }, null, e, IsEnabled: e.Refusal == null)).ToList();

    /// <summary>The ONE picker request of "+ Add step".</summary>
    public static PickerRequest Request() => new()
    {
        ContextKey = "rules.add-step",
        Title = "Add a step",
        Layout = PickerLayout.Standard,
        SelectionMode = PickerSelectionMode.Single,
        ItemsProvider = PickerEntries,
        ShowSidePane = false,
    };
}

/// <summary>What the host tells the step list each frame: where the pickers are, the run's step, whether the rows are editable.</summary>
public sealed class ActionListContext
{
    /// <summary>Opens the generic picker (CE-4168); null ⇒ <i>+ Add step</i> is disabled with its reason.</summary>
    public Action<PickerRequest, Action<PickerResult>>? OpenPicker { get; set; }
    /// <summary>The nested args forms' pick context (an entity / place field of a step).</summary>
    public IMapPickContext? Picks { get; set; }
    /// <summary>The running step, 1-based (<c>ObjectiveStatus.StepCur</c> while Acting); 0 = none.</summary>
    /// <summary>⭐ CE-4307 (RX5) — the names a step's links read as (null = keys / ids only).</summary>
    public Fdp.Toolkit.Scenario.LiveRefScope? Refs { get; set; }
    public int RunningStep { get; set; }
    /// <summary>The list's length as the run saw it (<c>StepCount</c>).</summary>
    public int StepCount { get; set; }
    /// <summary>A step that FAILED keeps its index in <see cref="RunningStep"/>; this is why (the status <c>Reason</c>).</summary>
    public string? FailedReason { get; set; }
    /// <summary>⭐ CE-4241 — a line under a step (what went to whom for a <c>Message</c>), or null; the host fills it from the world.</summary>
    public Func<ActionStep, string?>? NoteOf { get; set; }

    /// <summary>⭐ CE-4244 (MA-10 MP-2 b) — an <c>InsertMacro</c> step's preview, from the host: <c>Preview</c> shows its ghosts and returns the bar's text (null = could not), <c>Bar</c> is the text while one is open (null = none),
    /// <c>InsertNow</c> inserts it once (null = done, else why not), <c>Close</c> removes the ghosts; <c>InsertNowRefusal</c> says why <i>Insert now</i> is disabled (null = it is offered).</summary>
    public Func<ActionStep, string?>? Preview { get; set; }
    public Func<string?>? Bar { get; set; }
    public Func<ActionStep, string?>? InsertNow { get; set; }
    public Action? ClosePreview { get; set; }
    public Func<string?>? InsertNowRefusal { get; set; }
}

/// <summary>What the step list drew — the rails and the panel snapshot read it.</summary>
public sealed record ActionListProbe(IReadOnlyList<string> Rows, int Marked, string? MarkerText, bool AddEnabled, IReadOnlyList<string>? Notes = null);

/// <summary>
/// ⭐⭐ CE-4220 (rules view R5b, RE-4) — the do list as ROWS: one per step (its number and action, ▲ ▼ reorder, ✕ remove), each step's <c>args</c> drawn INLINE as a nested params document of the action's <see cref="ActionEntry.ParamsType"/> by the same
/// form machinery a task's params use (<see cref="BehaviorParamsForm.Open"/> / <see cref="ComponentEditDrawer"/> — ⛔ no second form, ⛔ no raw JSON). <i>+ Add step</i> opens the ONE generic picker over <see cref="ActionCatalogue"/>.
/// The standard leaf (<see cref="ActionListFieldEditor"/>) holds the whole <c>List&lt;ActionStep&gt;</c>; every edit builds a new list (<see cref="ActionListEdit"/>) and the document is replaced whole.
/// <para>⚠ Lives in <c>Hrot.Presentation</c>, not beside the editor in <c>Fdp.Presentation</c> (the design's RE-10): the picker (<c>NodeEditor.UI</c>) is not reachable from there. A STATEFUL drawer — the host keeps ONE instance per form so the nested sessions live across frames.</para>
/// </summary>
public sealed class ActionListFieldDrawer : IImGuiFieldDrawer, IDisposable
{
    private sealed class Row
    {
        public string Act = "";
        public string ArgsJson = "{}";          // the step's args as the document holds them (what a mismatch is told by)
        public ActionEntry? Entry;
        public IEditSession? Session;
        public string? Committed;               // the session's value as JSON when last opened / committed
        public string? Problem;
        public string Sentence = "";            // ⭐ CE-4307 (RX5) — the step as words (ActionSentence), built once when the row opens
        public ComponentEditDrawer? Drawer;     // ⭐ CE-3378 (N-1) — the args table's drawer, built once per row session (it was `new ComponentEditDrawer(session, picks, StandardDrawers(session))` EVERY frame)
        public IMapPickContext? DrawerPicks;    // the pick context the drawer was built with (rebuilt when the host's changes)
    }

    private readonly IComponentEditService _service;
    private readonly ActionListContext _ctx;
    private readonly List<Row> _rows = new();
    private ActionEntry? _pendingAdd;
    /// <summary>Where the buttons were drawn last frame (screen space) — a frame rail clicks them.</summary>
    internal (Vector2 Min, Vector2 Max) AddRect { get; private set; }
    internal List<(Vector2 Min, Vector2 Max)> UpRects { get; } = new();
    internal List<(Vector2 Min, Vector2 Max)> DownRects { get; } = new();
    internal List<(Vector2 Min, Vector2 Max)> RemoveRects { get; } = new();
    private ActionListProbe _probe = new(Array.Empty<string>(), 0, null, false);

    public ActionListFieldDrawer(IComponentEditService service, ActionListContext context)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _ctx = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Type TargetType => typeof(List<ActionStep>);

    /// <summary>What the last frame drew.</summary>
    public ActionListProbe Probe => _probe;

    public ActionListContext Context => _ctx;

    public void Dispose() { foreach (var r in _rows) r.Session?.Dispose(); _rows.Clear(); }

    /// <summary>Forgets the open step sessions (the form was reopened: a Revert, an undo, another rule).</summary>
    public void Reset() => Dispose();

    public bool DrawInput(ref object value, EditNode node)
    {
        var list = (value as List<ActionStep>) ?? new List<ActionStep>();
        bool changed = false;

        if (_pendingAdd is { } add) { list = ActionListEdit.Added(list, add); _pendingAdd = null; changed = true; }
        Sync(list);

        var shown = new List<string>(list.Count);
        var noted = new List<string>();
        UpRects.Clear(); DownRects.Clear(); RemoveRects.Clear();
        // the add button leads the list: a long list scrolls, and the button must stay reachable
        bool canAdd = _ctx.OpenPicker != null;
        if (!canAdd) ImGui.BeginDisabled();
        if (ImGui.Button("+ Add step"))
            _ctx.OpenPicker!(ActionListEdit.Request(), result => { if (result.First?.Tag is ActionEntry e) _pendingAdd = e; });
        AddRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (!canAdd) { ImGui.EndDisabled(); if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("this host has no picker"); }
        if (_ctx.RunningStep > 0 && _ctx.StepCount > 0) { ImGui.SameLine(); ImGui.TextDisabled($"step {_ctx.RunningStep} / {_ctx.StepCount}"); }

        if (list.Count == 0) ImGui.TextDisabled("(no steps: the trigger does nothing when it fires)");
        int? delete = null, up = null, down = null;
        for (int i = 0; i < list.Count; i++)
        {
            var row = _rows[i];
            ImGui.PushID(i);
            bool marked = _ctx.RunningStep == i + 1;
            DrawMarker(marked, marked && _ctx.FailedReason != null);
            ImGui.SameLine();
            // ⭐ CE-4307 — the buttons first, at a fixed place: the step's sentence can be long and would push them off the panel
            ImGui.TextUnformatted(Number(i));
            ImGui.SameLine();
            if (i == 0) ImGui.BeginDisabled();
            if (ImGui.SmallButton("^")) up = i;
            UpRects.Add((ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
            if (i == 0) ImGui.EndDisabled();
            ImGui.SameLine();
            if (i == list.Count - 1) ImGui.BeginDisabled();
            if (ImGui.SmallButton("v")) down = i;
            DownRects.Add((ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
            if (i == list.Count - 1) ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.SmallButton("x")) delete = i;
            RemoveRects.Add((ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove this step");
            ImGui.SameLine(0f, 12f);
            ImGui.TextWrapped(row.Sentence);   // ⭐ CE-4307 (RX5) — the step as words (ActionSentence)
            if (row.Entry != null && ImGui.IsItemHovered()) ImGui.SetTooltip(row.Entry.Fqn);
            if (marked && _ctx.FailedReason != null) ImGui.TextColored(new Vector4(0.9f, 0.4f, 0.35f, 1f), "failed: " + _ctx.FailedReason);
            if (_ctx.Preview != null && IsInsertMacro(list[i])) DrawPreviewBar(list[i], noted);   // ⭐ CE-4244
            if (_ctx.NoteOf?.Invoke(list[i]) is { } note) { ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]); ImGui.TextWrapped(note); ImGui.PopStyleColor(); noted.Add($"{i + 1}. {note}"); }   // ⭐ CE-4241
            shown.Add((marked ? "> " : "") + $"{i + 1}. {row.Sentence}" + (row.Problem != null ? $" ({row.Problem})" : ""));

            if (row.Problem == null && ActionListEdit.IsOrder(row.Act) && DrawOrderKind(list[i]) is { } kind)   // ⭐ CE-4307 (RX5) — Order by order KIND
            {
                list = ActionListEdit.WithOrderKind(list, i, kind);
                changed = true;
            }
            if (row.Problem != null) ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1f), row.Problem);
            else if (row.Session is { } session)
            {
                if (ImGui.BeginTable("##args", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                {
                    if (session.RebuildState == EditRebuildState.RebuildRequired) session.RebuildDocument();
                    if (row.Drawer == null || !ReferenceEquals(row.DrawerPicks, _ctx.Picks)) { row.Drawer = new ComponentEditDrawer(session, _ctx.Picks, defaults: row.Entry != null ? TypeDefaults.For(row.Entry.ParamsType) : null); row.DrawerPicks = _ctx.Picks; }
                    row.Drawer.DrawRoot(session.Document.Root);
                    ImGui.EndTable();
                }
                if (session.IsDirty && row.Entry != null)
                {
                    string json = BehaviorParamsForm.CommitToJson(session, row.Entry.ParamsType);
                    if (json != row.Committed)
                    {
                        row.Committed = json;
                        var args = ActionListEdit.MergeArgs(list[i].Args, json);
                        list = ActionListEdit.WithArgs(list, i, args);
                        row.ArgsJson = args.ToJsonString();
                        changed = true;
                    }
                }
            }
            ImGui.Separator();
            ImGui.PopID();
        }

        if (delete is { } d) { list = ActionListEdit.Removed(list, d); changed = true; }
        else if (up is { } u) { list = ActionListEdit.Moved(list, u, -1); changed = true; }
        else if (down is { } dn) { list = ActionListEdit.Moved(list, dn, +1); changed = true; }

        _probe = new ActionListProbe(shown, _ctx.RunningStep, _ctx.RunningStep > 0 && _ctx.StepCount > 0 ? $"step {_ctx.RunningStep} / {_ctx.StepCount}" : null, canAdd, noted);
        if (changed) value = list;
        return changed;
    }

    /// <summary>⭐ CE-4307 (RX5, DESIGN_Rule_Editor_UX §2.4) — the order kinds as the mission plan offers them (<c>OrderMenu.Orders</c>, the ONE list); a behaviour
    /// name stays writable in the form's own field below (Advanced). Returns the kind picked this frame, or null.</summary>
    private static string? DrawOrderKind(ActionStep step)
    {
        string? current = ActionListEdit.OrderKindOf(step);
        string? picked = null;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Order");   // ⭐ label FIRST (UI conventions §3.1 — LabelFirstFormsFrameRail), the combo after it
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo("##order-kind", current != null ? StructEdit.Core.Attributes.DisplayNames.Humanize(current) : "(a behaviour - Advanced)"))
        {
            foreach (var o in Hrot.Common.Orders.OrderMenu.Orders)
                if (ImGui.Selectable(StructEdit.Core.Attributes.DisplayNames.Humanize(o.Name), string.Equals(o.Name, current, StringComparison.OrdinalIgnoreCase))) picked = o.Name;
            ImGui.EndCombo();
        }
        return picked;
    }

    private static readonly string[] Numbers = BuildNumbers(64);
    private static string[] BuildNumbers(int n) { var a = new string[n]; for (int k = 0; k < n; k++) a[k] = (k + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "."; return a; }
    /// <summary>"n." without a per-frame allocation (R-2084).</summary>
    private static string Number(int i) => i < Numbers.Length ? Numbers[i] : (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";

    private static bool IsInsertMacro(ActionStep step) => step.Act == "InsertMacro" || step.Act.EndsWith(".InsertMacro", StringComparison.Ordinal);

    /// <summary>The probe's note of the preview buttons of the last frame (a rail reads it).</summary>
    internal string? PreviewButtons { get; private set; }

    /// <summary>⭐ CE-4244 (MP-2 b) — <i>Preview</i> on an <c>InsertMacro</c> step; while one is open its text, <i>Insert now</i> (live only) and <i>Close</i>. Nothing exists until <i>Insert now</i>.</summary>
    private void DrawPreviewBar(ActionStep step, List<string> noted)
    {
        string? bar = _ctx.Bar?.Invoke();
        if (ImGui.SmallButton("Preview")) _ctx.Preview!(step);
        PreviewRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (bar == null) { PreviewButtons = "Preview"; return; }
        string? refusal = _ctx.InsertNowRefusal?.Invoke() ?? (_ctx.InsertNow == null ? "this host cannot insert" : null);
        if (refusal != null) ImGui.BeginDisabled();
        ImGui.SameLine(); if (ImGui.SmallButton("Insert now")) _ctx.InsertNow?.Invoke(step);
        InsertRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (refusal != null) { ImGui.EndDisabled(); if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(refusal); }
        ImGui.SameLine(); if (ImGui.SmallButton("Close")) _ctx.ClosePreview?.Invoke();
        CloseRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.TextDisabled(bar);   // its own line: the panel is narrow, the buttons must stay reachable
        PreviewButtons = $"Preview | {bar} | Insert now{(refusal != null ? $" (disabled: {refusal})" : "")} | Close";
        noted.Add(PreviewButtons);
    }

    internal (Vector2 Min, Vector2 Max) PreviewRect { get; private set; }
    internal (Vector2 Min, Vector2 Max) InsertRect { get; private set; }
    internal (Vector2 Min, Vector2 Max) CloseRect { get; private set; }

    /// <summary>Keeps one row per step: a row whose act or args no longer match the document (a reorder, an undo, an external change) reopens its form.</summary>
    private void Sync(List<ActionStep> list)
    {
        while (_rows.Count > list.Count) { _rows[^1].Session?.Dispose(); _rows.RemoveAt(_rows.Count - 1); }
        for (int i = 0; i < list.Count; i++)
        {
            string act = list[i].Act, args = list[i].Args?.ToJsonString() ?? "{}";
            if (i < _rows.Count && _rows[i].Act == act && _rows[i].ArgsJson == args) continue;
            if (i < _rows.Count) { _rows[i].Session?.Dispose(); _rows[i] = Open(act, args); }
            else _rows.Add(Open(act, args));
        }
    }

    private Row Open(string act, string argsJson)
    {
        var row = new Row { Act = act, ArgsJson = argsJson, Entry = ActionListEdit.EntryOf(act) };
        try { row.Sentence = Hrot.UI.Common.Rules.ActionSentence.Of(new ActionStep { Act = act, Args = System.Text.Json.Nodes.JsonNode.Parse(argsJson) as System.Text.Json.Nodes.JsonObject }, _ctx.Refs); }
        catch (System.Text.Json.JsonException) { row.Sentence = act; }
        if (row.Entry == null) { row.Problem = $"unknown action '{act}'"; return row; }
        if (row.Entry.Refusal != null) { row.Problem = row.Entry.Refusal; return row; }
        try
        {
            row.Session = BehaviorParamsForm.Open(_service, row.Entry.ParamsType, argsJson);
            row.Committed = BehaviorParamsForm.ToJson(BehaviorParamsForm.Hydrate(row.Entry.ParamsType, argsJson), row.Entry.ParamsType);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { row.Problem = ex.Message; }
        return row;
    }

    /// <summary>The running-step marker: a filled triangle (the panel's font has no ▶), orange while running, red when the step failed.</summary>
    private static void DrawMarker(bool on, bool failed)
    {
        float h = ImGui.GetTextLineHeight();
        var p = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(h, h));
        if (!on) return;
        uint col = failed ? ImGui.GetColorU32(new Vector4(0.9f, 0.4f, 0.35f, 1f)) : ImGui.GetColorU32(new Vector4(0.94f, 0.56f, 0.29f, 1f));
        ImGui.GetWindowDrawList().AddTriangleFilled(new Vector2(p.X + 2, p.Y + 2), new Vector2(p.X + 2, p.Y + h - 2), new Vector2(p.X + h - 2, p.Y + h / 2f), col);
    }
}
