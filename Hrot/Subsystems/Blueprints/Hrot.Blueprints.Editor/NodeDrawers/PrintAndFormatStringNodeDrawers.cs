using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

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

    public bool IsDirty { get; private set; }

    public PrintStringNodeSession(PrintStringNode node, BlueprintAsset parentAsset, IEditService editService)
    {
        _node        = node        ?? throw new ArgumentNullException(nameof(node));
        _parent      = parentAsset ?? throw new ArgumentNullException(nameof(parentAsset));
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>Test hook: simulates the designer editing the "Format" text field.</summary>
    internal void SetFormatForTest(string format) => ApplyFormat(format);

    /// <summary>Test hook: simulates the designer picking a "Level" combo entry.</summary>
    internal void SetLevelForTest(BlueprintLogLevel level) => ApplyLevel(level);

    // ── Private mutation helpers (called by both Draw() and test hooks) ──────────

    /// <summary>
    /// <c>Format</c> drives <c>BuiltInNodeRegistry.PrintStringPins</c>'s derived arg-pin set, so
    /// this edit is structural (mirrors <c>GetSharedNodeSession.ApplySharedTypeId</c>).
    /// </summary>
    private void ApplyFormat(string format)
    {
        if (format == _node.Format) return;
        var before = _node.Format;
        _editService.RecordPropertyEdit(
            _parent, "Set Print Format",
            apply: () => { _node.Format = format; AfterFormatChange(); },
            undo:  () => { _node.Format = before; AfterFormatChange(); });
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
            ApplyFormat(format);
        ImGui.TextDisabled("{Name} placeholders become data-in pins, in first-appearance order.");

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

    public bool IsDirty { get; private set; }

    public FormatStringNodeSession(FormatStringNode node, BlueprintAsset parentAsset, IEditService editService)
    {
        _node        = node        ?? throw new ArgumentNullException(nameof(node));
        _parent      = parentAsset ?? throw new ArgumentNullException(nameof(parentAsset));
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>Test hook: simulates the designer editing the "Format" text field.</summary>
    internal void SetFormatForTest(string format) => ApplyFormat(format);

    /// <summary>Test hook: simulates the designer picking a "Result Type" combo entry.</summary>
    internal void SetResultTypeIdForTest(string resultTypeId) => ApplyResultTypeId(resultTypeId);

    // ── Private mutation helpers (called by both Draw() and test hooks) ──────────

    /// <summary>
    /// <c>Format</c> drives <c>BuiltInNodeRegistry.FormatStringPins</c>'s derived arg-pin set (plus
    /// the fixed "Result" out-pin), so this edit is structural.
    /// </summary>
    private void ApplyFormat(string format)
    {
        if (format == _node.Format) return;
        var before = _node.Format;
        _editService.RecordPropertyEdit(
            _parent, "Set Format Template",
            apply: () => { _node.Format = format; AfterStructuralChange(); },
            undo:  () => { _node.Format = before; AfterStructuralChange(); });
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
            ApplyFormat(format);
        ImGui.TextDisabled("{Name} placeholders become data-in pins, in first-appearance order.");

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
