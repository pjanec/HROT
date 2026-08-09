using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// BP-108 — Details-panel editor for <see cref="PrintStringNode"/>: the <c>Format</c> template and
/// the <see cref="BlueprintLogLevel"/> to write at. Mirrors <see cref="GetSharedNodeDrawer"/>'s
/// free-text-field + <see cref="ReturnNodeDrawer"/>'s enum-combo shapes.
///
/// <para>
/// ⭐ <c>Format</c> is structural: <c>BuiltInNodeRegistry.PrintStringPins</c> derives one data-in
/// pin per <c>{Name}</c> placeholder, so every edit to it must
/// <see cref="IEditService.NotifyStructureChanged"/> the same way <c>GetSharedNodeDrawer</c>'s
/// <c>SharedTypeId</c> edit does. <c>Level</c> is not pin-affecting — it only changes which level
/// probe the emitter guards the call with — so its edit skips the structural notification.
/// </para>
/// </summary>
public sealed class PrintStringNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;

    public PrintStringNodeDrawer(IEditService editService)
    {
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    public bool Handles(Node node) => node is PrintStringNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new PrintStringNodeSession((PrintStringNode)node, parentAsset, _editService);
}

/// <summary>
/// Edit session for <see cref="PrintStringNode"/>. Mutation logic lives in <see cref="ApplyFormat"/>/
/// <see cref="ApplyLevel"/>, reachable headlessly via <see cref="SetFormatForTest"/>/
/// <see cref="SetLevelForTest"/> (mirrors <c>GetSharedNodeSession</c>'s test-hook split);
/// <see cref="Draw"/> calls the exact same helpers and is the only ImGui-coupled surface.
/// </summary>
internal sealed class PrintStringNodeSession : INodeEditSession
{
    private readonly PrintStringNode _node;
    private readonly BlueprintAsset  _parent;
    private readonly IEditService    _editService;

    /// <summary>BP-204 — one undo entry per typing gesture, not per keystroke.</summary>
    private readonly ContinuousEditCoalescer<string> _formatCoalescer = new();

    public bool IsDirty { get; private set; }

    public PrintStringNodeSession(PrintStringNode node, BlueprintAsset parentAsset, IEditService editService)
    {
        _node        = node        ?? throw new ArgumentNullException(nameof(node));
        _parent      = parentAsset ?? throw new ArgumentNullException(nameof(parentAsset));
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>
    /// Test hook: simulates the designer typing into the "Format" field and finishing the gesture --
    /// i.e. the whole edit, recorded as ONE undo entry, exactly as <see cref="Draw"/> produces it.
    /// </summary>
    internal void SetFormatForTest(string format)
    {
        var before = _node.Format;
        LiveFormat(format);
        CommitFormat(before);
    }

    /// <summary>
    /// Test hook: the live half only -- the per-keystroke mutation, with no undo entry and no link
    /// pruning. Pair it with <see cref="CommitFormatForTest"/> to reproduce a multi-keystroke gesture.
    /// </summary>
    internal void LiveFormatForTest(string format) => LiveFormat(format);

    /// <summary>Test hook: the commit half -- see <see cref="CommitFormat"/>.</summary>
    internal void CommitFormatForTest(string beforeGesture) => CommitFormat(beforeGesture);

    /// <summary>Test hook: simulates the designer picking a "Level" combo entry.</summary>
    internal void SetLevelForTest(BlueprintLogLevel level) => ApplyLevel(level);

    /// <summary>Test hook: simulates the designer typing a placeholder's declared type.</summary>
    internal void SetArgTypeForTest(string name, string typeId) => ApplyArgType(name, typeId);

    // ── Private mutation helpers (called by both Draw() and test hooks) ──────────

    /// <summary>
    /// BP-204, the live half: mutates <c>Format</c> and re-projects, once per keystroke, with
    /// <b>no undo entry</b>. Pins appearing as the designer types is confirmed behaviour and is kept.
    /// </summary>
    private void LiveFormat(string format)
    {
        if (format == _node.Format) return;
        _node.Format = format;
        AfterFormatChange();
    }

    /// <summary>
    /// BP-204/BP-202, the commit half: records the <b>whole</b> gesture as one undo entry, and this is
    /// the only place links are pruned.
    ///
    /// <para>
    /// ⚠ <b>Pruning per keystroke would be catastrophic, which is why these two fixes are one fix.</b>
    /// Typing <c>{Threat}</c> passes through <c>{T</c>, <c>{Th</c>, <c>{Thr</c> … — each a different
    /// placeholder, so each a different derived pin. A prune on every keystroke would delete the wire
    /// on the first character of an edit that ends up restoring the very same pin. The pin set is
    /// allowed to churn freely during the gesture; only the endpoints are reconciled at the end.
    /// </para>
    /// </summary>
    private void CommitFormat(string beforeGesture)
    {
        var afterGesture = _node.Format;
        if (afterGesture == beforeGesture) return;

        var graph = DerivedPinMaintenance.FindOwningGraph(_parent, _node);

        // Links pruned when moving before -> after, and the reverse. Both directions can orphan a
        // link (a wire made to a pin that only exists under the NEW format dangles once undone), so
        // the transition is symmetric: restore what the opposite direction pruned, then prune.
        List<Link>? prunedForward = null;
        List<Link>? prunedBack    = null;

        _editService.RecordPropertyEdit(
            _parent, "Set Print Format",
            apply: () =>
            {
                // ⚠ Format is ALREADY the post-gesture value here: the widget mutated it live and
                // RecordPropertyEdit runs `apply` once at record time. So the "before" pin set has to
                // be reconstructed from beforeGesture rather than read off the node, or validBefore
                // would be the after-set and nothing would ever look vanished.
                _node.Format = beforeGesture;
                var validBefore = graph != null ? DerivedPinMaintenance.PinIds(_node) : null;
                _node.Format = afterGesture;
                DerivedPinMaintenance.ResyncPins(_node);
                if (graph != null)
                {
                    DerivedPinMaintenance.Restore(graph, prunedBack);
                    prunedForward = DerivedPinMaintenance.PruneVanished(graph, _node, validBefore!);
                }
                AfterFormatChange();
            },
            undo: () =>
            {
                _node.Format = afterGesture;
                var validBefore = graph != null ? DerivedPinMaintenance.PinIds(_node) : null;
                _node.Format = beforeGesture;
                DerivedPinMaintenance.ResyncPins(_node);
                if (graph != null)
                {
                    DerivedPinMaintenance.Restore(graph, prunedForward);
                    prunedBack = DerivedPinMaintenance.PruneVanished(graph, _node, validBefore!);
                }
                AfterFormatChange();
            });
    }

    /// <summary>
    /// BP-201: records a placeholder's declared type in <c>ArgTypes</c>, which is what types the
    /// derived data-in pin. Structural (the pin is retyped) but <b>never</b> pin-destroying, since a
    /// pin's identity comes from its name and direction — so no link can dangle and no prune is needed.
    /// </summary>
    private void ApplyArgType(string name, string typeId)
    {
        if (string.IsNullOrEmpty(name)) return;
        _node.ArgTypes.TryGetValue(name, out var before);
        if (string.Equals(before, typeId, StringComparison.Ordinal)) return;

        _editService.RecordPropertyEdit(
            _parent, $"Set Print Arg Type '{name}'",
            apply: () => { SetArg(name, typeId); AfterFormatChange(); },
            undo:  () => { SetArg(name, before); AfterFormatChange(); });
    }

    private void SetArg(string name, string? typeId)
    {
        if (string.IsNullOrEmpty(typeId)) _node.ArgTypes.Remove(name);
        else                              _node.ArgTypes[name] = typeId;
        DerivedPinMaintenance.ResyncPins(_node);
    }

    private void AfterFormatChange()
    {
        IsDirty = true;
        // Every Format edit changes the derived arg-pin set -- signal a STRUCTURAL change so the
        // canvas graph model re-projects (see GetSharedNodeSession.AfterChange's identical rationale).
        _editService.NotifyStructureChanged(_parent);
    }

    /// <summary>
    /// <c>Level</c> only picks which level probe the emitter guards the call with -- it never
    /// affects the pin set, so this edit skips the structural notification.
    /// </summary>
    private void ApplyLevel(BlueprintLogLevel level)
    {
        if (level == _node.Level) return;
        var before = _node.Level;
        _editService.RecordPropertyEdit(
            _parent, $"Set Print Level '{level}'",
            apply: () => { _node.Level = level; IsDirty = true; },
            undo:  () => { _node.Level = before; IsDirty = true; });
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Print String");
        ImGui.Separator();

        var format = _node.Format ?? "";
        if (ImGui.InputText("Format", ref format, 512))
            LiveFormat(format);

        // BP-204: the widget above mutates Format live (pins appear as you type -- confirmed
        // behaviour, kept), but undo is recorded once per gesture. The baseline is captured on
        // IsItemActivated because IsItemDeactivatedAfterEdit fires only after the value has changed.
        // Mirrors LiteralNodeDrawer's identical shape.
        _formatCoalescer.BeginIfNeeded(ImGui.IsItemActivated(), _node.Format ?? "");
        if (_formatCoalescer.TryCommit(ImGui.IsItemDeactivatedAfterEdit(), out var beforeGesture))
            CommitFormat(beforeGesture);

        ImGui.TextDisabled("{Name} placeholders become data-in pins, in first-appearance order.");

        FormatArgTypeRows.Draw(_node.Format, _node.ArgTypes, ApplyArgType);

        DrawLevelCombo();

        ImGui.TextDisabled(
            "Writes to the AI Behaviors log (AI.Behavior.Blueprint). A disabled level costs one bool read.");

        if (string.IsNullOrEmpty(_node.Format))
            ImGui.TextColored(EditorColors.Warning, "(empty Format -- nothing will be logged)");
    }

    private void DrawLevelCombo()
    {
        var names      = Enum.GetNames(typeof(BlueprintLogLevel));
        int currentIdx = (int)_node.Level;
        if (ImGui.Combo("Level", ref currentIdx, names, names.Length))
        {
            var chosen = (BlueprintLogLevel)currentIdx;
            if (chosen != _node.Level)
                ApplyLevel(chosen);
        }
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}

/// <summary>
/// BP-108 — Details-panel editor for <see cref="FormatStringNode"/>: the <c>Format</c> template and
/// the <c>ResultTypeId</c> (which <c>Fdp.Core.FixedString32/64/128</c> the result is sized to).
///
/// <para>
/// ⚠ <b>Truncation is silent at runtime</b> — Stage 2 cannot know a formatted result's actual
/// length at compile time, so a result longer than the chosen capacity is cut with no diagnostic
/// (see <see cref="FormatStringNode"/>'s own doc comment). This drawer is the only place a designer
/// can learn that, so <see cref="TruncationWarningForTest"/> is always rendered, not hidden behind a
/// hover tooltip.
/// </para>
/// </summary>
public sealed class FormatStringNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;

    public FormatStringNodeDrawer(IEditService editService)
    {
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    public bool Handles(Node node) => node is FormatStringNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new FormatStringNodeSession((FormatStringNode)node, parentAsset, _editService);
}

/// <summary>
/// Edit session for <see cref="FormatStringNode"/>. See <see cref="PrintStringNodeSession"/> for the
/// test-hook/mutation-helper split rationale.
/// </summary>
internal sealed class FormatStringNodeSession : INodeEditSession
{
    /// <summary>
    /// The three <c>ResultTypeId</c> choices, as the FULL FQN <see cref="FormatStringNode.ResultTypeId"/>
    /// is stored in (matches the node's own default <c>"Fdp.Core.FixedString128"</c> and the pin
    /// TypeId <c>BuiltInNodeRegistry.FormatStringPins</c> derives directly from it -- no short-name
    /// normalization happens at that call site, unlike Stage5_Schedule's emit-time prefixing).
    /// </summary>
    internal static readonly string[] ResultTypeOptions =
    {
        "Fdp.Core.FixedString32",
        "Fdp.Core.FixedString64",
        "Fdp.Core.FixedString128",
    };

    /// <summary>
    /// The always-visible truncation notice (task requirement: this drawer is the only place a
    /// designer can learn the result is silently cut at runtime -- Stage 2 cannot know a runtime
    /// length).
    /// </summary>
    internal const string TruncationWarningForTest =
        "Result is SILENTLY TRUNCATED to the chosen capacity -- a formatted string longer than " +
        "the ResultTypeId's capacity is cut with no diagnostic (Stage 2 cannot know a runtime length).";

    private readonly FormatStringNode _node;
    private readonly BlueprintAsset   _parent;
    private readonly IEditService     _editService;

    /// <summary>BP-204 — one undo entry per typing gesture, not per keystroke.</summary>
    private readonly ContinuousEditCoalescer<string> _formatCoalescer = new();

    public bool IsDirty { get; private set; }

    public FormatStringNodeSession(FormatStringNode node, BlueprintAsset parentAsset, IEditService editService)
    {
        _node        = node        ?? throw new ArgumentNullException(nameof(node));
        _parent      = parentAsset ?? throw new ArgumentNullException(nameof(parentAsset));
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>
    /// Test hook: the designer typing into "Format" and finishing the gesture — the whole edit as ONE
    /// undo entry, exactly as <see cref="Draw"/> produces it.
    /// </summary>
    internal void SetFormatForTest(string format)
    {
        var before = _node.Format;
        LiveFormat(format);
        CommitFormat(before);
    }

    /// <summary>Test hook: the live (per-keystroke) half only — no undo entry, no pruning.</summary>
    internal void LiveFormatForTest(string format) => LiveFormat(format);

    /// <summary>Test hook: the commit half — see <see cref="CommitFormat"/>.</summary>
    internal void CommitFormatForTest(string beforeGesture) => CommitFormat(beforeGesture);

    /// <summary>Test hook: simulates the designer picking a "Result Type" combo entry.</summary>
    internal void SetResultTypeIdForTest(string resultTypeId) => ApplyResultTypeId(resultTypeId);

    /// <summary>Test hook: simulates the designer declaring a placeholder's type.</summary>
    internal void SetArgTypeForTest(string name, string typeId) => ApplyArgType(name, typeId);

    // ── Private mutation helpers (called by both Draw() and test hooks) ──────────

    /// <summary>
    /// BP-204, the live half: mutates <c>Format</c> and re-projects once per keystroke, with no undo
    /// entry. <c>Format</c> drives <c>BuiltInNodeRegistry.FormatStringPins</c>'s derived arg-pin set
    /// (plus the fixed "Result" out-pin), so it is structural.
    /// </summary>
    private void LiveFormat(string format)
    {
        if (format == _node.Format) return;
        _node.Format = format;
        AfterStructuralChange();
    }

    /// <summary>
    /// BP-204/BP-202, the commit half — see <c>PrintStringNodeSession.CommitFormat</c> for why the
    /// link prune must happen here and never per keystroke.
    /// </summary>
    private void CommitFormat(string beforeGesture)
    {
        var afterGesture = _node.Format;
        if (afterGesture == beforeGesture) return;

        var graph = DerivedPinMaintenance.FindOwningGraph(_parent, _node);
        List<Link>? prunedForward = null;
        List<Link>? prunedBack    = null;

        _editService.RecordPropertyEdit(
            _parent, "Set Format Template",
            apply: () =>
            {
                // ⚠ Format is ALREADY the post-gesture value here: the widget mutated it live and
                // RecordPropertyEdit runs `apply` once at record time. So the "before" pin set has to
                // be reconstructed from beforeGesture rather than read off the node, or validBefore
                // would be the after-set and nothing would ever look vanished.
                _node.Format = beforeGesture;
                var validBefore = graph != null ? DerivedPinMaintenance.PinIds(_node) : null;
                _node.Format = afterGesture;
                DerivedPinMaintenance.ResyncPins(_node);
                if (graph != null)
                {
                    DerivedPinMaintenance.Restore(graph, prunedBack);
                    prunedForward = DerivedPinMaintenance.PruneVanished(graph, _node, validBefore!);
                }
                AfterStructuralChange();
            },
            undo: () =>
            {
                _node.Format = afterGesture;
                var validBefore = graph != null ? DerivedPinMaintenance.PinIds(_node) : null;
                _node.Format = beforeGesture;
                DerivedPinMaintenance.ResyncPins(_node);
                if (graph != null)
                {
                    DerivedPinMaintenance.Restore(graph, prunedForward);
                    prunedBack = DerivedPinMaintenance.PruneVanished(graph, _node, validBefore!);
                }
                AfterStructuralChange();
            });
    }

    /// <summary>
    /// BP-201: records a placeholder's declared type. Retypes the derived pin without changing its
    /// identity (a pin id is a function of name + direction), so no link can dangle.
    /// </summary>
    private void ApplyArgType(string name, string typeId)
    {
        if (string.IsNullOrEmpty(name)) return;
        _node.ArgTypes.TryGetValue(name, out var before);
        if (string.Equals(before, typeId, StringComparison.Ordinal)) return;

        _editService.RecordPropertyEdit(
            _parent, $"Set Format Arg Type '{name}'",
            apply: () => { SetArg(name, typeId); AfterStructuralChange(); },
            undo:  () => { SetArg(name, before); AfterStructuralChange(); });
    }

    private void SetArg(string name, string? typeId)
    {
        if (string.IsNullOrEmpty(typeId)) _node.ArgTypes.Remove(name);
        else                              _node.ArgTypes[name] = typeId;
        DerivedPinMaintenance.ResyncPins(_node);
    }

    /// <summary>
    /// <c>ResultTypeId</c> retypes the "Result" out-pin (and resizes the emitted stackalloc buffer),
    /// so this edit is ALSO structural — unlike <c>PrintStringNode.Level</c>, which never touches a pin.
    /// </summary>
    private void ApplyResultTypeId(string resultTypeId)
    {
        if (resultTypeId == _node.ResultTypeId) return;
        var before = _node.ResultTypeId;
        _editService.RecordPropertyEdit(
            _parent, $"Set Format Result Type '{resultTypeId}'",
            apply: () => { _node.ResultTypeId = resultTypeId; AfterStructuralChange(); },
            undo:  () => { _node.ResultTypeId = before;       AfterStructuralChange(); });
    }

    private void AfterStructuralChange()
    {
        IsDirty = true;
        _editService.NotifyStructureChanged(_parent);
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Format String");
        ImGui.Separator();

        var format = _node.Format ?? "";
        if (ImGui.InputText("Format", ref format, 512))
            LiveFormat(format);

        // BP-204: live per keystroke, one undo entry per gesture -- see PrintStringNodeSession.Draw.
        _formatCoalescer.BeginIfNeeded(ImGui.IsItemActivated(), _node.Format ?? "");
        if (_formatCoalescer.TryCommit(ImGui.IsItemDeactivatedAfterEdit(), out var beforeGesture))
            CommitFormat(beforeGesture);

        ImGui.TextDisabled("{Name} placeholders become data-in pins, in first-appearance order.");

        FormatArgTypeRows.Draw(_node.Format, _node.ArgTypes, ApplyArgType);

        DrawResultTypeCombo();

        ImGui.TextDisabled("(pure -- one \"Result\" data-out pin, no exec pins)");
        ImGui.TextColored(EditorColors.Warning, TruncationWarningForTest);
    }

    private void DrawResultTypeCombo()
    {
        var current    = string.IsNullOrEmpty(_node.ResultTypeId) ? ResultTypeOptions[2] : _node.ResultTypeId;
        var currentIdx = Array.IndexOf(ResultTypeOptions, current);
        var comboLabel = currentIdx >= 0 ? current : $"{current} (unrecognized)";

        if (ImGui.BeginCombo("Result Type", comboLabel))
        {
            for (int i = 0; i < ResultTypeOptions.Length; i++)
            {
                bool selected = i == currentIdx;
                if (ImGui.Selectable(ResultTypeOptions[i], selected))
                    ApplyResultTypeId(ResultTypeOptions[i]);
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
