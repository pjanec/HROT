using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Presentation.Behavior;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Map.Definitions.Behavior;
using Hrot.Presentation.Edits;
using NodeEditor.UI.Picker;
using ImGuiNET;
using StructEdit.Core;
using StructEdit.Reflection;

namespace Hrot.UI.Common.Rules;

/// <summary>One entry of a rule entity's brain journal (the Brain tab's own text — <c>BrainJournalText</c>).</summary>
public sealed record RuleJournalEntry(uint Seq, float At, string Kind, string Text);

/// <summary>
/// ⭐ CE-4220 (R5b, RE-7 live) — what a host with a LIVE run gives the rule editor: why a kind cannot be edited live (null = it can — the owner has a state keeper for it, <c>BehaviorRegistry.TryGetKeeper</c>), and the switch of the node feature
/// <c>ui.rules-live-values</c> (the owner samples a trigger's <c>when</c> leaves only while it is on; the editor turns it on while a When tab is open).
/// </summary>
public sealed record RuleLiveServices(Func<string, string?> EditRefusal, Action<bool> SetLiveValues);

/// <summary>⭐ R5c — <c>CreateRule</c> (tkb type, kind, params JSON) asks the host's ONE entity-creation path for a rule entity (+ Add); null ⇒ the menu is off.
/// What the host gives the rule editor: the ONE params form's service, the behaviour registry (the ONE params-type lookup), the edit-mode apply (the AI section's <c>EntityAiEditModel.ApplyTask</c>), the shared undo history, the journal reader — and, for R5b, the generic picker
/// (<i>+ Add step</i>) and the live-run services (null ⇒ a running rule stays read-only).</summary>
public sealed record RuleEditorServices(
    Func<IComponentEditService?> EditService, Func<Fdp.Toolkit.Behavior.BehaviorRegistry?> Registry,
    Func<Entity, string, string, bool> ApplyEditMode, EditHistory History,
    Func<Entity, IReadOnlyList<RuleJournalEntry>> Journal, Func<IMapPickContext?>? Picks = null,
    Func<Action<PickerRequest, Action<PickerResult>>?>? OpenPicker = null, RuleLiveServices? Live = null,
    Func<long, string, string, bool>? CreateRule = null, MacroPreviewServices? MacroPreview = null);

/// <summary>⭐ CE-4244 (MA-10 MP-2 b) — what the host gives the Do tab's <i>Preview</i> of an <c>InsertMacro</c> step: show the ghosts of the step of rule <c>Entity</c> (returns the bar text, null = nothing to show), the bar text while open,
/// <i>Insert now</i> (null = done, else why not), <i>Close</i>, and why <i>Insert now</i> is not offered (null = it is: a live exercise).</summary>
public sealed record MacroPreviewServices(Func<Entity, Fdp.Toolkit.ScenarioRules.Actions.ActionStep, string?> Preview, Func<string?> Bar,
    Func<Entity, Fdp.Toolkit.ScenarioRules.Actions.ActionStep, string?> InsertNow, Action Close, Func<string?> InsertNowRefusal);

/// <summary>What the editor drew, as text — the rails read it (and an agent through the panel snapshot). R5b adds the Do tab's rows, the running step, the live edit's state and the Live strip.</summary>
public sealed record RuleDetailsProbe(string Tab, IReadOnlyList<string> Tabs, IReadOnlyList<string> Fields, bool Editable, string? ReadOnlyReason, bool Dirty,
                                      string? Message, IReadOnlyList<string> History, IReadOnlyList<string> Results,
                                      IReadOnlyList<string>? Steps = null, string? StepMarker = null, string? LiveEdit = null, IReadOnlyList<string>? LiveStrip = null, bool LiveValuesWanted = false, IReadOnlyList<string>? Bindings = null);

/// <summary>
/// ⭐⭐ CE-4218 (rules view R5a; docs/DESIGN_Rules_View_UI.md §7 RE-1 … RE-7, A1) — the RULE EDITOR: the right pane of the rules window as a TABBED panel over the selected rule entity's ONE params document (<c>BehaviorParamsEditor</c> → <c>BehaviorParamsForm</c>,
/// the form the AI section and the mission panel draw — ⛔ no second form). <see cref="RuleTabs"/> chooses each tab's fields; the header (<c>common</c>) sits above every tab. <b>Apply / Revert</b> (no write per keystroke): in EDIT mode Apply is the AI section's
/// <c>AssignNow</c> on the scenario entity and pushes ONE entry to the shared <see cref="EditHistory"/> whose undo re-applies the previous JSON. <b>Read-only</b> (the fields drawn disabled, WITH the reason): a rule from a macro (A1: <i>from macro X — edit the macro or its args</i>,
/// with a jump to the instance), a rule that is RUNNING (<i>live edits come in R5b</i> — the RV-5 buttons keep working), a recording. <b>History</b> = the entity's brain journal; <b>Results</b> = its children with pass / fail (the summary's <c>Why</c>).
/// </summary>
public sealed class RuleDetails
{
    public const string LiveReadOnly = "live edits are not available on this host";
    /// <summary>The origin a live edit carries: a person at the instructor's console is a <see cref="BehaviorOrigin.Operator"/> (the journal shows <i>edited by Operator</i>).</summary>
    public const BehaviorOrigin LiveOrigin = BehaviorOrigin.Operator;

    private readonly RuleEditorServices _svc;
    private readonly Func<EntityRepository?> _world;
    private readonly Func<string?> _blocked;
    private readonly Action<int> _selectEntity;
    private readonly Action<RuleRow, double> _drawOverview;
    private readonly BehaviorParamsEditor _editor;
    private readonly ActionListContext _stepCtx = new();
    private readonly ActionListFieldDrawer _steps;
    /// <summary>⭐ U-15 / C3 (DESIGN_Condition_Editor §4) — the When tab's friendly view: the ONE condition editor (the Expert view is the struct editor, as before).</summary>
    private readonly Fdp.Presentation.Editing.ConditionEditor _conditions = new(Fdp.Toolkit.ReplayBrowser.Search.Conditions.ConditionContexts.Rule);
    private bool _expert;
    private int _boundRule;
    private string? _entityJson;           // the entity's params as the form was last opened from them: the form reopens only when THIS changes (an Apply, an undo, a live edit landing) — never because the form itself was edited
    private string? _pending;
    private RuleTab? _requestTab;
    private readonly Dictionary<int, RuleTab> _tabOf = new();
    private readonly Dictionary<int, RuleTab> _shownTab = new();   // ⭐ CE-4286 — the tab ImGui's bar was last told to show, per rule (a different one in a frame is a click)
    private readonly Dictionary<int, LiveEdit> _liveEdits = new();
    private int _editSeq;
    private bool _liveValuesOn;

    /// <summary>The write of a live edit — <see cref="RuleCommands.Edit"/>, the ONE path the DebugApi's <c>/objectives/{id}/edit</c> also ends in. A seam only so a rail can see what was written without a write router in its world.</summary>
    internal delegate Fdp.Toolkit.Replication.Patching.EntityWriteRoute LiveEditWriter(EntityRepository world, Entity entity, string paramsJson, int editOrigin, ref int lastSeq, out int seq, out Guid requestId);
    internal LiveEditWriter LiveWriter { get; set; } = RuleCommands.Edit;
    /// <summary>Wall clock for the "owner did not answer" wait (a seam for rails).</summary>
    internal Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;
    /// <summary>⭐ CE-2380 part 2 — how long a live edit may stay without ANY outcome before the form says the owner did not answer.</summary>
    public static readonly TimeSpan OwnerAnswerWait = TimeSpan.FromSeconds(5);
    private IReadOnlyList<LiveLeafRow> _strip = Array.Empty<LiveLeafRow>();
    private RuleDetailsProbe _probe = new("", Array.Empty<string>(), Array.Empty<string>(), false, null, false, null, Array.Empty<string>(), Array.Empty<string>());

    /// <summary>A live edit this editor sent: the sequence it used, the journal's length then (the owner's answer is the next entry), the JSON, whether the trigger was acting (the owner HOLDS the edit until its list ends), and the verdict once it came.</summary>
    private sealed class LiveEdit
    {
        public int Seq; public Guid RequestId; public DateTime SentAt; public string Json = ""; public bool WasActing; public string? Verdict; public bool Done;
    }

    public RuleDetails(RuleEditorServices services, Func<EntityRepository?> world, Func<string?> blocked, Action<int> selectEntity, Action<RuleRow, double> drawOverview)
    {
        _svc = services ?? throw new ArgumentNullException(nameof(services));
        _world = world; _blocked = blocked; _selectEntity = selectEntity; _drawOverview = drawOverview;
        var service = _svc.EditService() ?? throw new InvalidOperationException("The rule editor needs the params form's edit service.");
        _editor = new BehaviorParamsEditor(service);
        _steps = new ActionListFieldDrawer(service, _stepCtx);
    }

    public RuleDetailsProbe Probe => _probe;

    /// <summary>The do list's drawer (its probe: rows, marker; the rails read it).</summary>
    internal ActionListFieldDrawer Steps => _steps;

    /// <summary>Where the Undo button was drawn (screen space) — a frame rail clicks it.</summary>
    internal (Vector2 Min, Vector2 Max) UndoRect { get; private set; }
    public string? Message { get; private set; }
    public RuleTab CurrentTab(RuleRow row) => _tabOf.TryGetValue(row.NetworkId, out var t) && RuleTabs.TabsFor(row.Status.Role).Contains(t) ? t : RuleTabs.TabsFor(row.Status.Role)[0];
    public void RequestTab(RuleTab tab) => _requestTab = tab;

    /// <summary>The entity's authored behaviour name and params JSON (its <c>BehaviorStartRecord</c>), or null.</summary>
    public (string Name, string Json)? ParamsOf(RuleRow row) => RuleSentence.ParamsOf(_world(), row.Entity);   // ⭐ U-18 — the ONE reader, shared with the List view

    /// <summary>A row with a run behind it: its <c>ObjectiveStatus</c> is replicated (an authored row, before any run, has none).</summary>
    public static bool IsLive(RuleRow row) => !row.Authored;

    /// <summary>Why the fields cannot be edited, or null when they can (edit mode, an own rule — or, in a live run, a kind the owner can re-parameterise).</summary>
    public string? ReadOnlyReason(RuleRow row)
    {
        if (_blocked() is { } b) return b;
        if (row.Macro != null) return $"from macro {row.Macro} — edit the macro or its args";                 // A1
        if (ParamsOf(row) is not { } p) return IsLive(row) && _svc.Live != null ? "the rule's params are not on this node" : IsLive(row) ? LiveReadOnly : "this rule has no authored params on this node";
        if (BehaviorParamsForm.ParamsTypeOf(p.Name, _svc.Registry()) is null) return $"{p.Name} has no params form";
        if (IsLive(row))
        {
            if (_svc.Live is not { } live) return LiveReadOnly;
            if (live.EditRefusal(p.Name) is { } why) return why;                                                // ⭐ RE-7 — a kind with no state keeper refuses: say so up front
        }
        return null;
    }

    /// <summary>Binds the form to <paramref name="row"/>'s params and settles the probe (no ImGui) — <see cref="Draw"/> calls it first; a rail may call it alone.</summary>
    public void Prepare(RuleRow row, double now)
    {
        if (row.NetworkId != _boundRule) { _boundRule = row.NetworkId; _pending = null; _entityJson = null; _editor.Sync(null, null); _steps.Reset(); }
        var p = ParamsOf(row);
        var type = p is { } pp ? BehaviorParamsForm.ParamsTypeOf(pp.Name, _svc.Registry()) : null;
        if (p is { } pr && type != null)
        {
            // ⭐ reopen the form only when the ENTITY's params changed (an Apply landing, an undo, a live edit applied by the owner) — ⛔ not on every call: this runs twice a frame, and an edit the form holds is not the entity's yet
            if (_entityJson != pr.Json || _editor.Session == null || _editor.Type != type)
            { _entityJson = pr.Json; _editor.Sync(null, null); _editor.Sync(type, pr.Json); _steps.Reset(); _pending = null; }
            if (_editor.Edited() is { } edited) _pending = edited;
        }
        else { _editor.Sync(null, null); _entityJson = null; }
        var tab = CurrentTab(row);
        var fields = type == null ? Array.Empty<string>() : (tab == RuleTab.Overview || tab == RuleTab.Status ? Array.Empty<string>() : RuleTabs.FieldsOf(type, tab).ToArray());
        string? ro = ReadOnlyReason(row);
        bool dirty = _pending != null && p is { } cur && _pending != cur.Json;
        SettleLiveEdit(row);
        _strip = LiveStripOf(row, type, tab);
        FeedStepContext(row);
        _probe = new RuleDetailsProbe(tab.ToString(), RuleTabs.TabsFor(row.Status.Role).Select(RuleTabs.TitleOf).ToList(),
            (type != null && tab == RuleTab.Settings ? new[] { RuleTabs.HeaderField } : Array.Empty<string>()).Concat(fields).ToList(), ro == null, ro, dirty, Message,
            tab == RuleTab.History ? _svc.Journal(row.Entity).Select(j => $"{j.At:0.0}s {j.Text}").ToList() : (IReadOnlyList<string>)Array.Empty<string>(),
            tab == RuleTab.Results && _world() is { } w ? MissionSummaryModel.Build(w, new SummaryOptions(SummarySide.Both), null, now).Why(row.NetworkId).Select(x => $"{x.Title} | {(x.Go is { } g ? (g ? "pass" : "fail") : "—")}").ToList() : (IReadOnlyList<string>)Array.Empty<string>(),
            Steps: tab == RuleTab.Do ? _steps.Probe.Rows : null, StepMarker: tab == RuleTab.Do ? _steps.Probe.MarkerText : null,
            LiveEdit: LiveEditLine(row), LiveStrip: _strip.Select(StripText).ToList(), LiveValuesWanted: WantsLiveValues(row, tab),
            Bindings: tab == RuleTab.Bindings ? BindingsOf(row).Select(b => $"{b.Field} <- {b.Param}").ToList() : null);
    }

    private void FeedStepContext(RuleRow row)
    {
        var s = row.Status;
        bool trigger = s.Role == ObjectiveRole.Trigger && !row.Authored;
        bool acting = trigger && s.State == ObjectiveState.Active && s.TriggerState == TriggerState.Acting;
        _stepCtx.RunningStep = trigger ? s.StepCur : 0;
        _stepCtx.StepCount = trigger ? s.StepCount : 0;
        _stepCtx.FailedReason = trigger && s.StepCur > 0 && !acting ? (s.Reason.ToString() is { Length: > 0 } r ? r : "a step failed") : null;   // a failed step keeps its index (the owner zeroes it on the next transition)
        int source = row.NetworkId;
        _stepCtx.NoteOf = step => MessageDelivery.StepNote(_world(), source, step);   // ⭐ CE-4241 — what went to whom, under each Message step
        if (_svc.MacroPreview is { } mp)   // ⭐ CE-4244 — the Preview / Insert now / Close of an InsertMacro step
        {
            var entity = row.Entity;
            _stepCtx.Preview = step => mp.Preview(entity, step);
            _stepCtx.Bar = mp.Bar;
            _stepCtx.InsertNow = step => mp.InsertNow(entity, step);
            _stepCtx.ClosePreview = mp.Close;
            _stepCtx.InsertNowRefusal = mp.InsertNowRefusal;
        }
        _stepCtx.OpenPicker = _svc.OpenPicker?.Invoke();
        _stepCtx.Picks = _svc.Picks?.Invoke();
    }

    // ── RE-3 live: the Live strip ────────────────────────────────────────────────────────────────────────────────────────

    private bool WantsLiveValues(RuleRow row, RuleTab tab)
        => IsLive(row) && _svc.Live != null && row.Status.Role == ObjectiveRole.Trigger && tab == RuleTab.When && _blocked() == null;

    private IReadOnlyList<LiveLeafRow> LiveStripOf(RuleRow row, Type? type, RuleTab tab)
    {
        if (!WantsLiveValues(row, tab) || type == null || _world() is not { } w || ParamsOf(row) is not { } p) return Array.Empty<LiveLeafRow>();
        SearchPredicateDto? when = null;
        try { if (BehaviorParamsForm.Hydrate(type, _pending ?? p.Json) is TriggerParamsJsonDto t) when = t.When; } catch (Exception ex) when (ex is InvalidCastException or NullReferenceException) { }
        return LiveLeafOverlay.Match(LiveLeafOverlay.Read(w, row.Entity), when);
    }

    private static string StripText(LiveLeafRow r)
        => r.Sampled ? $"{(r.Holds ? "yes" : "no")} | {r.Label} | {r.Measured:0.##}{(r.InForm ? "" : " | not in the form")}" : $"- | {r.Label} | not sampled yet";

    /// <summary>⭐ RE-3 live — the node feature <c>ui.rules-live-values</c> is on while a When tab of a live trigger is open, off otherwise (and when the window closes: <see cref="Idle"/>).</summary>
    private void SetLiveValues(bool on)
    {
        if (_liveValuesOn == on) return;
        _liveValuesOn = on;
        _svc.Live?.SetLiveValues(on);
    }

    /// <summary>The window is not drawn (closed): the live values are not wanted.</summary>
    public void Idle() => SetLiveValues(false);

    /// <summary>Whether this editor has the live-values feature switched on.</summary>
    public bool LiveValuesOn => _liveValuesOn;

    // ── RE-7 live: Apply = RuleCommands.Edit ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ CE-2380 part 2 (RE-7, one mechanism) — the owner's verdict on the last live edit, read each frame from the edit-outcome log by the request id the write returned: <i>Pending</i> (acting ⇒ held until the do list ends) ·
    /// <i>Applied</i> · <i>Refused</i> with the WHOLE reason · no outcome at all for <see cref="OwnerAnswerWait"/> ⇒ the owner did not answer. Replaces the journal-watching / params-compare bookkeeping.
    /// </summary>
    private void SettleLiveEdit(RuleRow row)
    {
        if (!_liveEdits.TryGetValue(row.NetworkId, out var le) || le.Done) return;
        var w = _world();
        if (w != null && Fdp.Toolkit.Replication.Edits.EditOutcomeLog.Of(w).TryGet(le.RequestId, out var o))
        {
            switch (o.Status)
            {
                case Fdp.Toolkit.Replication.Edits.EditStatus.Refused: le.Done = true; le.Verdict = "refused - " + o.Reason; return;
                case Fdp.Toolkit.Replication.Edits.EditStatus.Applied: le.Done = true; le.Verdict = "applied - the rule's state is kept, this run only"; return;
                default: le.Verdict = le.WasActing ? "pending (acting) - held until the do list ends" : "sent - the owner is judging it"; return;
            }
        }
        le.Verdict = Now() - le.SentAt > OwnerAnswerWait ? "the owner did not answer" : "sent - waiting for the owner";
        if (Now() - le.SentAt > OwnerAnswerWait) le.Done = true;
    }

    private string? LiveEditLine(RuleRow row) => _liveEdits.TryGetValue(row.NetworkId, out var le) ? le.Verdict : null;

    /// <summary>⭐ RE-7 live — sends the edited params to the rule's owner through <see cref="RuleCommands.Edit"/> (the DebugApi's <c>POST /objectives/{id}/edit</c> writes the same). ⛔ Not pushed to the scenario's <see cref="EditHistory"/>: a live edit lasts this run only (SR-109).</summary>
    private bool ApplyLive(RuleRow row, string json)
    {
        var w = _world();
        if (w == null || !w.IsAlive(row.Entity)) { Message = "the rule is gone"; return false; }
        bool acting = row.Status.Role == ObjectiveRole.Trigger && row.Status.State == ObjectiveState.Active && row.Status.TriggerState == TriggerState.Acting;
        var route = LiveWriter(w, row.Entity, json, (int)LiveOrigin, ref _editSeq, out int seq, out var requestId);
        if (route == Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused) { Message = "nothing was written: this node has no way to ask the owner"; return false; }
        _liveEdits[row.NetworkId] = new LiveEdit { Seq = seq, RequestId = requestId, SentAt = Now(), Json = json, WasActing = acting };
        SettleLiveEdit(row);
        Message = route == Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Direct ? "live edit sent" : "live edit requested of the owner";
        return true;
    }

    /// <summary>⭐ RE-7 — writes the edited params: EDIT mode = the scenario entity (the AI section's <c>ApplyTask</c> → <c>AssignNow</c>) with ONE undo entry; a LIVE run = <see cref="RuleCommands.Edit"/> (the rule keeps its state, this run only). False (and a message) when refused or there is nothing to apply.</summary>
    public bool Apply(RuleRow row)
    {
        if (ReadOnlyReason(row) is { } ro) { Message = ro; return false; }
        if (_pending is not { } json || ParamsOf(row) is not { } cur || json == cur.Json) { Message = "nothing to apply"; return false; }
        if (IsLive(row)) return ApplyLive(row, json);
        string before = cur.Json, name = cur.Name;
        var entity = row.Entity;
        // ⭐ CE-4249 — a brand-new trigger has no `when` yet and the runtime refuses it ("'when' is required") with the reason only in the LOG: say it here, before the assignment
        if (name == RuleAdd.TriggerKind && System.Text.Json.Nodes.JsonNode.Parse(json) is System.Text.Json.Nodes.JsonObject o && o["when"] is null) { Message = "refused: a trigger needs a When — pick a condition on the When tab, then Apply"; return false; }
        if (!_svc.ApplyEditMode(entity, name, json)) { Message = "refused: the behaviour did not accept these params (the reason is in the log)"; return false; }
        _svc.History.Push($"edit {row.Title}", () => _svc.ApplyEditMode(entity, name, json), () => _svc.ApplyEditMode(entity, name, before));
        _pending = null;
        Message = $"applied — undo: \"edit {row.Title}\"";
        return true;
    }

    /// <summary>
    /// ⭐ CE-4222 (RE-9 + A2) — dropping <paramref name="from"/> on the trigger <paramref name="trigger"/>: "from is done" is ANDed into the trigger's <c>when</c> IN THE FORM DOCUMENT (the form reopens on the new JSON, the When tab is shown) and stays a pending edit —
    /// <b>Apply</b> writes it through RE-7 (so it is ONE undo entry on the shared <see cref="EditHistory"/>). Edit mode only; refused (with the reason, written to <see cref="Message"/>) for a live run, a recording, a rule from a macro (A1), a non-trigger, or a rule it already waits on.
    /// </summary>
    public string? ConnectInto(RuleRow trigger, RuleRow from)
    {
        string? Refuse(string why) { Message = why; return why; }
        if (trigger.NetworkId == from.NetworkId) return Refuse("a rule cannot wait on itself");
        if (trigger.Status.Role != ObjectiveRole.Trigger) return Refuse("only a trigger waits on another rule");
        if (IsLive(trigger)) return Refuse("connecting changes the scenario: edit mode only");
        if (ReadOnlyReason(trigger) is { } ro) return Refuse(ro);
        if (ParamsOf(trigger) is not { } p || BehaviorParamsForm.ParamsTypeOf(p.Name, _svc.Registry()) is not { } type) return Refuse("this rule has no params form");
        // the form of THIS trigger: bind to it (a drop on a row that was not selected), carrying its pending edit if it is the one open
        string baseJson = trigger.NetworkId == _boundRule && _pending != null ? _pending : p.Json;
        if (_world() is not { } w) return Refuse("no world");
        if (RuleConnect.AddedTo(baseJson, from, RuleConnect.LinkOf(from, AuthoredRules.ScopeOf(w)), out string? why) is not { } added) return Refuse(why ?? "nothing to add");
        _boundRule = trigger.NetworkId; _entityJson = p.Json; _steps.Reset();
        _pending = BehaviorParamsForm.ToJson(BehaviorParamsForm.Hydrate(type, added), type);   // the form's own serialisation, as an edit made in it would give
        _editor.Sync(null, null); _editor.Sync(type, _pending);
        _tabOf[trigger.NetworkId] = RuleTab.When;
        Message = $"{from.Title} added to the When of {trigger.Title}: Apply to keep it";
        return null;
    }

    /// <summary>Test hook — stages <paramref name="json"/> as the form's pending edit (what a drawn field edit produces).</summary>
    internal void StagePending(string json) => _pending = json;

    /// <summary>Drops the edits not applied: the form reopens from the entity's params.</summary>
    public void Revert(RuleRow row)
    {
        _pending = null; _entityJson = null; _steps.Reset();
        _liveEdits.Remove(row.NetworkId);
        Message = "reverted";
    }

    /// <summary>Undo / redo through the ONE history (the Parameters window's too).</summary>
    public bool Undo() { bool ok = _svc.History.Undo(); Message = ok ? "undone" : "nothing to undo"; _pending = null; _entityJson = null; _steps.Reset(); return ok; }
    public bool Redo() { bool ok = _svc.History.Redo(); Message = ok ? "redone" : "nothing to redo"; _pending = null; _entityJson = null; _steps.Reset(); return ok; }

    // ── drawing ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    public void Draw(RuleRow row, double now)
    {
        Prepare(row, now);
        string? ro = ReadOnlyReason(row);
        var p = ParamsOf(row);
        var type = p is { } pp ? BehaviorParamsForm.ParamsTypeOf(pp.Name, _svc.Registry()) : null;

        if (ro != null) ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.3f, 1f), ro);
        if (row.Macro != null && row.MacroInstance != 0 && ImGui.SmallButton("Go to the macro instance")) _selectEntity(row.MacroInstance);

        // ⭐ CE-4286 / U-2 — a real tab bar on the shared icon tab (no close box, neutral palette, a coloured icon each). ImGui keeps its own selected tab: when the MODEL's tab changed from outside (a request, another row)
        //   it is pushed with SetSelected; a frame where ImGui shows a different tab WITHOUT a push is the user's click.
        var cur = CurrentTab(row);
        if (_requestTab is { } req && RuleTabs.TabsFor(row.Status.Role).Contains(req)) { _tabOf[row.NetworkId] = req; cur = req; _requestTab = null; }
        bool push = !_shownTab.TryGetValue(row.NetworkId, out var shown) || shown != cur;
        RuleTab? clicked = null;
        if (NodeEditor.Core.Widgets.IconTab.BeginBar("##rule-tabs"))
        {
            foreach (var tab in RuleTabs.TabsFor(row.Status.Role))
            {
                var flags = push && tab == cur ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                if (NodeEditor.Core.Widgets.IconTab.Begin(RuleTabs.IdOf(tab), RuleTabs.TitleOf(tab), NodeEditor.Core.Widgets.Icons.Get(RuleTabs.IconKeyOf(tab)), flags))
                {
                    if (!push && tab != cur) clicked = tab;
                    ImGui.EndTabItem();
                }
            }
            NodeEditor.Core.Widgets.IconTab.EndBar();
        }
        if (clicked is { } c) { _tabOf[row.NetworkId] = c; cur = c; }
        _shownTab[row.NetworkId] = cur;
        SetLiveValues(WantsLiveValues(row, cur));                 // ⭐ RE-3 live: on while a When tab of a live trigger is open
        ImGui.BeginChild("##rule-tab-body", new Vector2(0, Math.Max(120f, ImGui.GetContentRegionAvail().Y - ApplyBarHeight(row, ro))), ImGuiChildFlags.None, ImGuiWindowFlags.None);
        DrawTab(row, cur, type, ro, now);
        ImGui.EndChild();
        DrawApplyBar(row, ro);
    }

    private static readonly HashSet<string> HeaderFields = new() { RuleTabs.HeaderField };

    private void DrawTab(RuleRow row, RuleTab tab, Type? type, string? ro, double now)
    {
        switch (tab)
        {
            case RuleTab.Overview: case RuleTab.Status: _drawOverview(row, now); return;
            case RuleTab.Bindings: DrawBindings(row, type, ro); return;
            case RuleTab.History:
                var entries = _svc.Journal(row.Entity);
                if (entries.Count == 0) ImGui.TextDisabled("No journal (the rule is not running on this node).");
                foreach (var j in entries) ImGui.TextUnformatted($"{j.At:0.0}s  {j.Text}");
                return;
            case RuleTab.Results:
                var why = _world() is { } w ? MissionSummaryModel.Build(w, new SummaryOptions(SummarySide.Both), null, now).Why(row.NetworkId) : Array.Empty<WhyRow>();
                if (why.Count == 0) ImGui.TextDisabled("Nothing under it.");
                foreach (var x in why)
                    ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(x.Go is { } g ? (g ? RulesModel.Achieved : RulesModel.Failed) : RulesModel.Grey),
                                      $"{x.Title}  {(x.IsMeasure ? $"{x.Value:0.##} vs {x.Target:0.##}" : x.Score.ToString("0.00"))}  weight {x.Weight:0.##}{(x.Required ? "  required ✓" : "")}");
                return;
        }
        if (type == null || _editor.Session is not { } session) { ImGui.TextDisabled(ro ?? "no params form"); return; }
        var picks = _svc.Picks?.Invoke();
        if (ro != null) ImGui.BeginDisabled();
        if (tab == RuleTab.Settings)
        {
            // ⭐ U-9 (§8.2 ⑧) — title, scoring and visibility live on Settings only; the When tab is the condition and its timing
            BehaviorParamsForm.DrawFields(session, picks, "##rule-common", null, HeaderFields);
            ImGui.Spacing();
        }
        var tabFields = RuleTabs.FieldsOf(type, tab).ToHashSet();
        if (tab == RuleTab.When && tabFields.Contains(RuleTabs.WhenField))
        {
            // ⭐ U-15 / C3 — the condition as sentence rows (the friendly view); "Expert view" shows today's tree for the same DTO
            ImGui.Checkbox("Expert view", ref _expert);
            if (!_expert && WhenNode(session) is { } when)
            {
                // ⭐ C4 — the Live column: the owner's samples of the form's own leaves, in the plan's order (RE-3, matched by label)
                _conditions.Live = WantsLiveValues(row, tab) ? LiveSamples() : null;
                var dto = when.Binding?.GetBoxed() as Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto;
                if (_conditions.Draw("rule-when", ref dto, picks)) { when.Binding?.SetBoxed(dto); session.MarkStructuralChange(); }
                if (_conditions.ExpertRequested) { _expert = true; _conditions.ExpertRequested = false; }
                tabFields.Remove(RuleTabs.WhenField);
                ImGui.Spacing();
                // ⭐ C4 — the timing as the END OF THE SENTENCE (§4): "…and it stays true for 30 s, then wait 0 s. Fire once"
                DrawTimingLine(session);
                foreach (var f in TimingFields) tabFields.Remove(f);
                ImGui.Spacing();
            }
        }
        BehaviorParamsForm.DrawFields(session, picks, "##rule-" + tab, null, tabFields, new IImGuiFieldDrawer[] { _steps });
        if (ro != null) ImGui.EndDisabled();
        if (tab == RuleTab.When && WantsLiveValues(row, tab)) DrawLiveStrip(onlyOutOfForm: !_expert);
    }

    // ── R5c: the Bindings tab (MA-1 / MA-2, third home) ──────────────────────────────────────────────────────────────────

    /// <summary>The prefix the params form's bindings carry in <c>ParamBindings</c> (the binder's own path form, <c>Behavior.Params.Speed</c>).</summary>
    public const string BindPrefix = "Behavior.Params";

    /// <summary>One binding of the rule entity: the field path (relative to the params) and the macro parameter that feeds it.</summary>
    public readonly record struct BindingRow(string Field, string Param);

    /// <summary>The rule entity's <c>ParamBindings</c> (component 469) that name its params: <c>"Behavior.Params.&lt;field&gt;" → param</c>, by field.</summary>
    public IReadOnlyList<BindingRow> BindingsOf(RuleRow row)
    {
        var w = _world();
        if (w == null || !w.IsAlive(row.Entity) || !w.TryGetTable(typeof(Fdp.Toolkit.Scenario.ParamBindings), out _) || !w.HasManagedComponent<Fdp.Toolkit.Scenario.ParamBindings>(row.Entity)) return Array.Empty<BindingRow>();
        var pb = ((Fdp.ModuleHost.Abstractions.ISimulationView)w).GetManagedComponentRO<Fdp.Toolkit.Scenario.ParamBindings>(row.Entity);
        return pb == null ? Array.Empty<BindingRow>()
             : pb.Bindings.Where(kv => kv.Key.StartsWith(BindPrefix + ".", StringComparison.Ordinal)).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                 .Select(kv => new BindingRow(kv.Key[(BindPrefix.Length + 1)..], kv.Value)).ToList();
    }

    /// <summary>The bind context of the rule entity's params form — ⭐ the very one the AI section's task form uses (<c>EntityBindContext</c>: writes a NEW <c>ParamBindings</c> on the entity, params in scope = the resident header's). Null without a world.</summary>
    public IParamBindContext? BindsOf(RuleRow row)
        => _world() is { } w && !row.Entity.IsNull
            ? new EntityBindContext(new Fdp.Presentation.Adapters.RepositoryAdapter(w), row.Entity, BindPrefix, () => HeaderParams.Of(w)) : null;

    /// <summary>A1 — only a macro FILE's own entities are bindable: a rule expanded from a macro instance, a live run and a recording are read-only (the reason is <see cref="ReadOnlyReason"/>'s).</summary>
    private void DrawBindings(RuleRow row, Type? type, string? ro)
    {
        var rows = BindingsOf(row);
        ImGui.TextDisabled(rows.Count == 0 ? "No field is fed from a parameter." : "Fields fed from a parameter");
        foreach (var b in rows) ImGui.TextUnformatted($"{b.Field}   <-   {b.Param}");
        var w = _world();
        if (w != null && HeaderParams.Of(w).Count == 0) ImGui.TextDisabled("No parameters are declared here: a macro file declares them (open the macro).");
        if (type == null || _editor.Session is not { } session) { ImGui.TextDisabled(ro ?? "no params form"); return; }
        ImGui.Spacing();
        if (ro != null) ImGui.BeginDisabled();
        BehaviorParamsForm.DrawFields(session, _svc.Picks?.Invoke(), "##rule-bind", BindsOf(row), RuleTabs.MembersOf(type).Where(m => m != "Do").ToHashSet());
        if (ro != null) ImGui.EndDisabled();
    }

    /// <summary>The params form's <c>When</c> node (the trigger's condition), or null.</summary>
    private static StructEdit.Core.EditNode? WhenNode(StructEdit.Core.IEditSession session)
    {
        var root = session.Document.Root;
        if (root.Kind == StructEdit.Core.EditNodeKind.SelectionRoot && root.Children.Count == 1) root = root.Children[0];
        foreach (var c in root.Children) if (c.Name == RuleTabs.WhenField) return c;
        return null;
    }

    private readonly List<Fdp.Presentation.Editing.ConditionEditor.LiveSample> _liveSamples = new();

    /// <summary>⭐ C4 — the strip's in-form leaves as the editor's samples (the strip is already laid over the form's leaves, in order).</summary>
    private IReadOnlyList<Fdp.Presentation.Editing.ConditionEditor.LiveSample> LiveSamples()
    {
        _liveSamples.Clear();
        foreach (var r in _strip) if (r.InForm) _liveSamples.Add(new(r.Sampled, r.Holds, r.Measured));
        return _liveSamples;
    }

    /// <summary>⭐ C4 — the trigger's timing fields the sentence's ending edits (the form below keeps the rest, e.g. <i>fire if true when armed</i>).</summary>
    private static readonly string[] TimingFields = { "HoldFor", "Delay", "Cooldown", "Repeat", "MaxFires", "WhileTrue" };

    private static StructEdit.Core.EditNode? FieldNode(StructEdit.Core.IEditSession session, string name)
    {
        var root = session.Document.Root;
        if (root.Kind == StructEdit.Core.EditNodeKind.SelectionRoot && root.Children.Count == 1) root = root.Children[0];
        foreach (var c in root.Children) if (c.Name == name) return c;
        return null;
    }

    /// <summary>⭐ C4 — <i>…and it stays true for [30 s], then wait [0 s]. Fire [once]</i>: words + chips over the session's own fields (one undo through Apply).</summary>
    private void DrawTimingLine(StructEdit.Core.IEditSession session)
    {
        var hold = FieldNode(session, "HoldFor"); var delay = FieldNode(session, "Delay"); var cool = FieldNode(session, "Cooldown");
        var repeat = FieldNode(session, "Repeat"); var max = FieldNode(session, "MaxFires"); var whileTrue = FieldNode(session, "WhileTrue");
        if (hold == null || delay == null || repeat == null) return;
        float F(StructEdit.Core.EditNode? n) => n?.Binding?.GetBoxed() is float f ? f : 0f;
        bool B(StructEdit.Core.EditNode? n) => n?.Binding?.GetBoxed() is bool b && b;
        int I(StructEdit.Core.EditNode? n) => n?.Binding?.GetBoxed() is int i ? i : 0;
        ImGui.PushID("timing");
        ImGui.TextUnformatted("...and it stays true for");
        ImGui.SameLine(); SecondsChip("##hold", hold, F(hold));
        ImGui.SameLine(0f, 2f); ImGui.TextUnformatted(", then wait");
        ImGui.SameLine(); SecondsChip("##delay", delay, F(delay));
        ImGui.SameLine(0f, 2f); ImGui.TextUnformatted(". Fire");
        ImGui.SameLine();
        TimingSentence = $"...and it stays true for {F(hold):0.##} s, then wait {F(delay):0.##} s. Fire {FireWords(B(repeat), B(whileTrue), F(cool), I(max))}";
        if (Fdp.Presentation.Editing.ReferenceLeafDrawers.DrawChip("##fire", FireWords(B(repeat), B(whileTrue), F(cool), I(max)), null, 360f)) ImGui.OpenPopup("fire");
        if (ImGui.BeginPopup("fire"))
        {
            if (ImGui.Selectable("once", !B(repeat))) repeat.Binding?.SetBoxed(false);
            if (ImGui.Selectable("each time it becomes true", B(repeat) && !B(whileTrue))) { repeat.Binding?.SetBoxed(true); whileTrue?.Binding?.SetBoxed(false); }
            if (whileTrue != null && ImGui.Selectable("again and again while it holds", B(repeat) && B(whileTrue))) { repeat.Binding?.SetBoxed(true); whileTrue.Binding?.SetBoxed(true); }
            if (B(repeat))
            {
                ImGui.Separator();
                float c = F(cool);
                ImGui.SetNextItemWidth(140f);
                if (cool != null && Fdp.Presentation.Editing.NumberInput.Float(ref c, "s")) cool.Binding?.SetBoxed(Math.Max(0f, c));
                ImGui.SameLine(); ImGui.TextDisabled("at most every");
                int m = I(max);
                ImGui.SetNextItemWidth(140f);
                if (max != null && ImGui.InputInt("times at most (0 = no limit)", ref m)) max.Binding?.SetBoxed(Math.Max(0, m));
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private static void SecondsChip(string id, StructEdit.Core.EditNode node, float seconds)
    {
        if (Fdp.Presentation.Editing.ReferenceLeafDrawers.DrawChip(id, $"{seconds:0.##} s", null, 120f)) ImGui.OpenPopup(id + "p");
        if (!ImGui.BeginPopup(id + "p")) return;
        float v = seconds;
        ImGui.SetNextItemWidth(140f);
        if (Fdp.Presentation.Editing.NumberInput.Float(ref v, "s")) node.Binding?.SetBoxed(Math.Max(0f, v));
        ImGui.EndPopup();
    }

    /// <summary><i>once</i> · <i>each time it becomes true, at most every 10 s, up to 3 times</i> · <i>every 10 s while it holds</i>.</summary>
    internal static string FireWords(bool repeat, bool whileTrue, float cooldown, int maxFires)
    {
        if (!repeat) return "once";
        string s = whileTrue ? $"every {cooldown:0.##} s while it holds" : "each time it becomes true" + (cooldown > 0 ? $", at most every {cooldown:0.##} s" : "");
        return maxFires > 0 ? $"{s}, up to {maxFires} times" : s;
    }

    /// <summary>⭐ C4 — the timing sentence as drawn (rails).</summary>
    internal string? TimingSentence { get; private set; }

    /// <summary>⭐ C4 — each condition row's live text (rails).</summary>
    internal IReadOnlyList<string?> ConditionLive => _conditions.RowLive;

    /// <summary>⭐ U-15 — the friendly view's sentence rows (rails, the panel probe).</summary>
    internal IReadOnlyList<string> ConditionRows => _conditions.Sentences;

    /// <summary>⭐ U-15 — the first unfilled blank of the condition (Apply stays disabled while it is set), or null.</summary>
    internal string? ConditionProblem => _expert ? null : _conditions.Problem;

    /// <summary>RE-3 live — the Live strip under the When form: each leaf, whether it holds and the number behind it, matched by label.</summary>
    private void DrawLiveStrip(bool onlyOutOfForm = false)
    {
        if (onlyOutOfForm)
        {
            // ⭐ C4 — the friendly view shows each row's live result in its own column; only what the form does not hold (an applied edit) remains here
            bool any = false;
            foreach (var r in _strip) any |= !r.InForm;
            if (!any) { if (_strip.Count == 0) ImGui.TextDisabled("Live: waiting for the owner's samples (about twice a second)..."); return; }
        }
        ImGui.Spacing();
        ImGui.TextDisabled("Live");
        if (_strip.Count == 0) { ImGui.TextDisabled("waiting for the owner's samples (about twice a second)..."); return; }
        foreach (var r in _strip)
        {
            if (onlyOutOfForm && r.InForm) continue;
            if (!r.Sampled) { ImGui.TextDisabled($"-    {r.Label}   not sampled yet"); continue; }
            ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(r.Holds ? RulesModel.Achieved : RulesModel.Failed), r.Holds ? "yes" : "no ");
            ImGui.SameLine();
            ImGui.TextUnformatted($"{r.Label}   {r.Measured:0.##}{(r.InForm ? "" : "   (applied; not in the form)")}");
        }
    }

    /// <summary>⭐ Part 0 a — the height the Apply bar needs THIS frame: the separator, the button row, and each text line it will draw (wrapped to the pane's width) — never a fixed reserve that clips the verdict or the message.</summary>
    private float ApplyBarHeight(RuleRow row, string? ro)
    {
        float w = ImGui.GetContentRegionAvail().X;
        float Lines(string? t) => t == null ? 0f : ImGui.CalcTextSize(t, false, w).Y + ImGui.GetStyle().ItemSpacing.Y;
        bool live = IsLive(row);
        return ImGui.GetStyle().ItemSpacing.Y * 2 + 2f + ImGui.GetFrameHeight()
             + (live && ro == null ? Lines(LiveNote) : 0f) + (live ? Lines(LiveEditLine(row)) : 0f) + Lines(Message) + 6f;
    }

    /// <summary>Where the Apply bar's last line ended (screen y): a rail asserts it is inside the pane.</summary>
    internal float BarBottom { get; private set; }

    private const string LiveNote = "a live edit lasts this run only: not saved with the scenario, not in Undo";

    private void DrawApplyBar(RuleRow row, string? ro)
    {
        ImGui.Separator();
        bool live = IsLive(row);
        bool dirty = _probe.Dirty;
        bool blank = ConditionProblem != null;   // ⭐ U-15 — an unfilled blank: the rule cannot be applied (§4)
        if (ro != null || !dirty || blank) ImGui.BeginDisabled();
        if (ImGui.Button(live ? "Apply live" : "Apply")) Apply(row);
        if (ro != null || !dirty || blank) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ro != null || !dirty) ImGui.BeginDisabled();
        if (ImGui.Button("Revert")) Revert(row);
        if (ro != null || !dirty) ImGui.EndDisabled();
        if (ro != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(ro);
        ImGui.SameLine();
        // ⛔ read CanUndo / CanRedo ONCE: a click changes them between Begin- and EndDisabled, and ImGui asserts (found live: the editor died on the first Undo click)
        // a live run: the scenario's undo history is not this edit's (a live edit lasts this run only) ⇒ the buttons are off
        bool canUndo = !live && _svc.History.CanUndo, canRedo = !live && _svc.History.CanRedo;
        string undoLabel = _svc.History.UndoLabel is { } ul ? $"Undo {ul}" : "Undo";
        if (!canUndo) ImGui.BeginDisabled();
        bool undo = ImGui.Button(undoLabel);
        UndoRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (!canUndo) ImGui.EndDisabled();
        ImGui.SameLine();
        if (!canRedo) ImGui.BeginDisabled();
        bool redo = ImGui.Button("Redo");
        if (!canRedo) ImGui.EndDisabled();
        if (undo) Undo();
        if (redo) Redo();
        if (live && ro == null) { ImGui.PushTextWrapPos(0f); ImGui.TextDisabled(LiveNote); ImGui.PopTextWrapPos(); }
        if (live && LiveEditLine(row) is { } line)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(line.StartsWith("refused", StringComparison.Ordinal) ? new Vector4(0.9f, 0.4f, 0.35f, 1f) : new Vector4(0.9f, 0.7f, 0.3f, 1f), line);
            ImGui.PopTextWrapPos();
        }
        if (Message != null) { ImGui.PushTextWrapPos(0f); ImGui.TextDisabled(Message); ImGui.PopTextWrapPos(); }
        BarBottom = ImGui.GetCursorScreenPos().Y;
    }
}
