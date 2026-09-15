using System.Linq;
using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// BP-07 — Details-panel editor for <see cref="CallCustomEventNode.EventId"/>.
///
/// <para>
/// ⚠ <b>The audit's suggested source was wrong.</b> BP-07 says to reuse
/// <c>UnifiedEventDiscovery.All()</c>, but that enumerates <c>[BlueprintEvent]</c> C# structs and
/// editor-authored <em>engine</em> events — the vocabulary <c>WaitForEvent</c> and the When node's
/// EventFired mode use. A custom event is <b>asset-scoped</b>: <c>NodePinSchema.CallCustomEventPins</c>
/// resolves <c>EventId</c> against <c>asset.CustomEvents</c>, and Stage5 does the same. Offering
/// engine events here would have produced a picker whose every choice failed to resolve.
/// </para>
///
/// <para>
/// The value is written as the declaration's GUID: pin projection parses it with
/// <c>Guid.TryParse</c>. (Stage5's <c>FindCustomEventIndex</c> also accepts a bare Name, so
/// hand-authored assets keep working — but the picker writes the canonical form.)
/// </para>
/// </summary>
public sealed class CallCustomEventNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;

    public CallCustomEventNodeDrawer(IEditService editService)
    {
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    public bool Handles(Node node) => node is CallCustomEventNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new CallCustomEventNodeSession((CallCustomEventNode)node, parentAsset, _editService);
}

/// <summary>
/// Edit session for <see cref="CallCustomEventNode"/>. Mutation and list logic live in helpers with
/// internal test hooks; <see cref="Draw"/> is the only ImGui-coupled surface.
/// </summary>
internal sealed class CallCustomEventNodeSession : INodeEditSession
{
    private readonly CallCustomEventNode _node;
    private readonly BlueprintAsset      _parent;
    private readonly IEditService        _editService;

    public bool IsDirty { get; private set; }

    public CallCustomEventNodeSession(
        CallCustomEventNode node, BlueprintAsset parentAsset, IEditService editService)
    {
        _node        = node;
        _parent      = parentAsset;
        _editService = editService;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>Test hook: simulates the designer picking a custom event.</summary>
    internal void SetEventIdForTest(string eventId) => ApplyEventId(eventId);

    /// <summary>Test hook: the asset's declared custom events, in declaration order.</summary>
    internal IReadOnlyList<CustomEventDecl> GetAvailableEventsForTest()
        => _parent.CustomEvents;

    /// <summary>Test hook: the display label the picker shows for a declaration.</summary>
    internal static string LabelForTest(CustomEventDecl decl) => Label(decl);

    /// <summary>
    /// Test hook: true when the node's current <see cref="CallCustomEventNode.EventId"/> is
    /// non-empty but resolves to no declaration on this asset — a dangling reference the picker
    /// must surface rather than silently blank.
    /// </summary>
    internal bool IsCurrentEventUnresolvedForTest() => ResolveCurrent() is null && !string.IsNullOrEmpty(_node.EventId);

    // ── Private helpers (called by both Draw() and test hooks) ─────────────────

    /// <summary>
    /// Resolves the node's stored id to a declaration. Accepts both forms Stage5 accepts — a GUID
    /// (what this picker writes and what pin projection needs) and a bare Name (hand-authored
    /// assets) — so opening such an asset shows the event as resolved rather than dangling.
    /// </summary>
    private CustomEventDecl? ResolveCurrent()
    {
        if (string.IsNullOrEmpty(_node.EventId)) return null;

        if (Guid.TryParse(_node.EventId, out var id))
            return _parent.CustomEvents.FirstOrDefault(e => e.Id == id);

        return _parent.CustomEvents.FirstOrDefault(
            e => string.Equals(e.Name, _node.EventId, StringComparison.Ordinal));
    }

    private static string Label(CustomEventDecl decl)
    {
        var name = string.IsNullOrEmpty(decl.Name) ? "(unnamed)" : decl.Name;
        return decl.Parameters.Count == 0
            ? name
            : $"{name} ({string.Join(", ", decl.Parameters.Select(p => p.Name))})";
    }

    private void ApplyEventId(string eventId)
    {
        if (eventId == _node.EventId) return;

        var before = _node.EventId;
        _editService.RecordPropertyEdit(
            _parent, "Set Custom Event",
            apply: () => { _node.EventId = eventId; AfterChange(); },
            undo:  () => { _node.EventId = before;  AfterChange(); });
    }

    /// <summary>
    /// The chosen event's parameters become this node's data-IN pins
    /// (<c>NodePinSchema.CallCustomEventPins</c>), so changing it is a STRUCTURAL edit — the canvas
    /// must re-project. Data-driven: the composition root wires the refresh, this drawer never
    /// references the canvas.
    /// </summary>
    private void AfterChange()
    {
        IsDirty = true;
        _editService.NotifyStructureChanged(_parent);
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Call Custom Event");
        ImGui.Separator();

        var events   = _parent.CustomEvents;
        var current  = ResolveCurrent();
        var unresolved = IsCurrentEventUnresolvedForTest();

        if (events.Count == 0)
        {
            ImGui.TextColored(EditorColors.Warning,
                "(this Blueprint declares no custom events — add one in the My Blueprint panel)");
            return;
        }

        var comboLabel = current is not null ? Label(current)
                       : unresolved          ? $"{_node.EventId} (unresolved)"
                       : "(none)";

        if (ImGui.BeginCombo("Event", comboLabel))
        {
            if (unresolved)
            {
                ImGui.Selectable($"{_node.EventId} (current — not declared here)", true);
                ImGui.Separator();
            }

            foreach (var decl in events)
            {
                bool selected = current is not null && decl.Id == current.Id;
                if (ImGui.Selectable(Label(decl), selected))
                    ApplyEventId(decl.Id.ToString("D"));
                if (selected) ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (unresolved)
            ImGui.TextColored(EditorColors.Warning,
                $"(no custom event on this Blueprint matches '{_node.EventId}' — kept as-is)");
        else if (current is { Parameters.Count: > 0 })
            ImGui.TextDisabled($"({current.Parameters.Count} argument pin(s) projected from the declaration)");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
