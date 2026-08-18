using System.Linq;
using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Blueprints.Editor.NodeDrawers;

public sealed class WhenNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IChannelCommandCatalog _channelCatalog;
    private readonly IEngineEventCatalog _eventCatalog;
    private readonly IEditService _editService;
    private readonly IPredicateCompiler _predicateCompiler;

    public WhenNodeDrawer(
        IChannelCommandCatalog channelCatalog,
        IEngineEventCatalog eventCatalog,
        IEditService editService,
        IPredicateCompiler predicateCompiler)
    {
        _channelCatalog    = channelCatalog    ?? throw new ArgumentNullException(nameof(channelCatalog));
        _eventCatalog      = eventCatalog      ?? throw new ArgumentNullException(nameof(eventCatalog));
        _editService       = editService       ?? throw new ArgumentNullException(nameof(editService));
        _predicateCompiler = predicateCompiler ?? throw new ArgumentNullException(nameof(predicateCompiler));
    }

    public bool Handles(Node node) => node is WhenNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new WhenNodeSession(
            (WhenNode)node, parentAsset,
            _channelCatalog, _eventCatalog, _editService, _predicateCompiler);
}

internal sealed class WhenNodeSession : INodeEditSession
{
    private readonly WhenNode _node;
    private readonly BlueprintAsset _parent;
    private readonly IChannelCommandCatalog _channelCatalog;
    private readonly IEngineEventCatalog _eventCatalog;
    private readonly IEditService _editService;
    private readonly IPredicateCompiler _predicateCompiler;

    // ImGui view-state only (the EventFired picker's incremental filter box).
    private string _eventFilterText = "";

    public bool IsDirty { get; private set; }

    public WhenNodeSession(
        WhenNode node,
        BlueprintAsset parentAsset,
        IChannelCommandCatalog channelCatalog,
        IEngineEventCatalog eventCatalog,
        IEditService editService,
        IPredicateCompiler predicateCompiler)
    {
        _node              = node;
        _parent            = parentAsset;
        _channelCatalog    = channelCatalog;
        _eventCatalog      = eventCatalog;
        _editService       = editService;
        _predicateCompiler = predicateCompiler;
    }

    public void Draw()
    {
        ImGui.Text("When");
        ImGui.Separator();
        DrawDispatchGuard();
        DrawModeSelector();
        ImGui.Separator();

        switch (_node.Mode)
        {
            case WhenMode.ValueChanged: DrawValueChangedForm(); break;
            case WhenMode.EventFired:   DrawEventFiredForm();   break;
            case WhenMode.ConditionMet: DrawConditionMetForm(); break;
            case WhenMode.EqsResult:    DrawEqsResultForm();    break;
        }

        ImGui.Separator();
        DrawEdgeSelector();
        ImGui.Separator();
        DrawPreviewPill();
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>
    /// Test hook: sets the node mode and marks the session dirty, simulating what
    /// DrawModeSelector() does when the user picks a different mode.
    /// </summary>
    internal void SetModeForTest(WhenMode mode)
    {
        _node.Mode = mode;
        IsDirty = true;
    }

    // ── Private draw helpers ─────────────────────────────────────────────────────

    private void DrawDispatchGuard()
    {
        if (_parent.Dispatch != BlueprintDispatchKind.Instance)
        {
            ImGui.TextColored(EditorColors.Error,
                "⚠ WhenNode is only allowed in Instance Blueprints.");
            ImGui.Separator();
        }
    }

    private void DrawModeSelector()
    {
        int modeIdx = (int)_node.Mode;
        string[] labels = { "Value Changed", "Event Fired", "Condition Met", "EQS Result" };
        if (ImGui.Combo("Mode", ref modeIdx, labels, labels.Length))
        {
            _node.Mode = (WhenMode)modeIdx;
            IsDirty = true;
        }
    }

    private void DrawEdgeSelector()
    {
        ImGui.Text("Edges:");
        ImGui.SameLine();

        bool rising  = _node.Edges.HasFlag(WhenEdge.RisingEdge);
        bool falling = _node.Edges.HasFlag(WhenEdge.FallingEdge);

        if (ImGui.Checkbox("Rising",  ref rising))
        {
            _node.Edges = rising
                ? _node.Edges | WhenEdge.RisingEdge
                : _node.Edges & ~WhenEdge.RisingEdge;
            IsDirty = true;
        }
        ImGui.SameLine();
        if (ImGui.Checkbox("Falling", ref falling))
        {
            _node.Edges = falling
                ? _node.Edges | WhenEdge.FallingEdge
                : _node.Edges & ~WhenEdge.FallingEdge;
            IsDirty = true;
        }

        if (_node.Edges == WhenEdge.None)
            ImGui.TextColored(EditorColors.Warning, "(no edge selected — WhenNode will never fire)");
    }

    private void DrawPreviewPill()
    {
        string preview = _node.Mode switch
        {
            WhenMode.ValueChanged => _node.ValueChanged is { } vc
                ? $"Changed: {vc.ComponentTypeId}.{vc.PropertyPath}"
                : "(unconfigured)",
            WhenMode.EventFired => _node.EventFired is { } ef
                ? $"Event: {ef.EventTypeId}"
                : "(unconfigured)",
            WhenMode.ConditionMet => "(predicate)",
            WhenMode.EqsResult => _node.EqsResult is { } er
                ? $"EQS {er.Trigger}: {er.SensorVariableName}"
                : "(unconfigured)",
            _ => "(unconfigured)",
        };
        ImGui.TextDisabled($"Preview: {preview}");
    }

    private void DrawValueChangedForm()
    {
        _node.ValueChanged ??= new ValueChangedPayload();
        ImGui.TextDisabled("(Value Changed form — component/property picker)");
    }

    // ── BP-10: EventFired form ────────────────────────────────────────────────

    /// <summary>Test hook: the engine events the catalog exposes.</summary>
    internal IReadOnlyList<EngineEventCatalogEntry> GetAvailableEventsForTest()
        => _eventCatalog.GetEntries();

    /// <summary>Test hook: events matching a filter over display name, name and type FQN.</summary>
    internal IReadOnlyList<EngineEventCatalogEntry> GetFilteredEventsForTest(string filterText)
    {
        var all = _eventCatalog.GetEntries();
        if (string.IsNullOrEmpty(filterText)) return all;
        return all.Where(e =>
                e.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase)
                || e.DisplayName.Contains(filterText, StringComparison.OrdinalIgnoreCase)
                || e.EventTypeFqn.Contains(filterText, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Test hook: simulates the designer picking an event to subscribe to.</summary>
    internal void SetEventTypeIdForTest(string eventTypeId) => ApplyEventTypeId(eventTypeId);

    /// <summary>Test hook: simulates toggling "only when the payload targets me".</summary>
    internal void SetTargetFilterForTest(EventTargetFilter filter) => ApplyTargetFilter(filter);

    /// <summary>
    /// Test hook: true when an event type is named but no catalog entry matches — a subscription
    /// that can never fire, which the form must surface rather than render as ordinary.
    /// </summary>
    internal bool IsCurrentEventUnlistedForTest()
    {
        var id = _node.EventFired?.EventTypeId;
        if (string.IsNullOrEmpty(id)) return false;
        return ResolveEntry(id!) is null;
    }

    private EngineEventCatalogEntry? ResolveEntry(string eventTypeId)
        => _eventCatalog.GetEntries().FirstOrDefault(
               e => string.Equals(e.EventTypeFqn, eventTypeId, StringComparison.Ordinal))
           ?? _eventCatalog.GetEntries().FirstOrDefault(
               e => string.Equals(e.Name, eventTypeId, StringComparison.Ordinal));

    private static string EventLabel(EngineEventCatalogEntry e)
        => string.IsNullOrEmpty(e.DisplayName) ? e.Name : e.DisplayName;

    private void ApplyEventTypeId(string eventTypeId)
    {
        var payload = _node.EventFired ??= new EventFiredPayload();
        if (eventTypeId == payload.EventTypeId) return;

        var beforeType  = payload.EventTypeId;
        var beforeField = payload.TargetFieldName;

        // The target field belongs to the event's own payload shape, so it cannot survive a change
        // of event. Adopt the new entry's declared field in the same edit — one gesture, one entry.
        var afterField = ResolveEntry(eventTypeId)?.TargetFieldName;

        _editService.RecordPropertyEdit(
            _parent, $"Subscribe to '{eventTypeId}'",
            apply: () =>
            {
                payload.EventTypeId     = eventTypeId;
                payload.TargetFieldName = string.IsNullOrEmpty(afterField) ? null : afterField;
                IsDirty = true;
            },
            undo: () =>
            {
                payload.EventTypeId     = beforeType;
                payload.TargetFieldName = beforeField;
                IsDirty = true;
            });
    }

    private void ApplyTargetFilter(EventTargetFilter filter)
    {
        var payload = _node.EventFired ??= new EventFiredPayload();
        if (filter == payload.TargetFilter) return;

        var before = payload.TargetFilter;
        _editService.RecordPropertyEdit(
            _parent, $"Set Event Target Filter {filter}",
            apply: () => { payload.TargetFilter = filter; IsDirty = true; },
            undo:  () => { payload.TargetFilter = before; IsDirty = true; });
    }

    /// <summary>
    /// BP-10 — the real form. The catalog was already injected and already queried here; only the
    /// result was never rendered, so the node could be placed but never pointed at an event.
    /// </summary>
    private void DrawEventFiredForm()
    {
        var payload  = _node.EventFired ??= new EventFiredPayload();
        var entries  = _eventCatalog.GetEntries();
        var current  = string.IsNullOrEmpty(payload.EventTypeId) ? null : ResolveEntry(payload.EventTypeId);
        var unlisted = IsCurrentEventUnlistedForTest();

        if (entries.Count == 0 && !unlisted)
        {
            ImGui.TextColored(EditorColors.Warning, "(no engine events discovered)");
            return;
        }

        var comboLabel = current is not null ? EventLabel(current)
                       : unlisted            ? $"{payload.EventTypeId} (unlisted)"
                       : "(none)";

        if (ImGui.BeginCombo("Event", comboLabel))
        {
            ImGui.InputTextWithHint("##WhenEventFilter", "Filter...", ref _eventFilterText, 256);

            if (unlisted)
            {
                ImGui.Selectable($"{payload.EventTypeId} (current — not in catalog)", true);
                ImGui.Separator();
            }

            foreach (var e in GetFilteredEventsForTest(_eventFilterText))
            {
                bool selected = current is not null && ReferenceEquals(e, current);
                if (ImGui.Selectable($"{EventLabel(e)}##{e.EventTypeFqn}", selected))
                    ApplyEventTypeId(e.EventTypeFqn);
                if (selected) ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (unlisted)
        {
            ImGui.TextColored(EditorColors.Warning,
                $"(no catalog entry matches '{payload.EventTypeId}' — this subscription can never fire)");
        }
        else if (current is not null)
        {
            ImGui.TextDisabled($"{current.EventTypeFqn}");

            // Self-filtering needs a field on the payload naming the target entity. Offer the toggle
            // only when the catalog says the event has one — otherwise "Self" would silently mean
            // "everything", which is the opposite of what the checkbox promises.
            if (!string.IsNullOrEmpty(current.TargetFieldName))
            {
                bool selfOnly = payload.TargetFilter == EventTargetFilter.Self;
                if (ImGui.Checkbox("Only when it targets me", ref selfOnly))
                    ApplyTargetFilter(selfOnly ? EventTargetFilter.Self : EventTargetFilter.None);
                ImGui.SameLine();
                ImGui.TextDisabled($"(via {current.TargetFieldName})");
            }
            else
            {
                ImGui.TextDisabled("(this event carries no target field — fires for every entity)");
            }

            if (current.QoS == EventQoS.BestEffort)
                ImGui.TextColored(EditorColors.Warning,
                    "(BestEffort delivery — this event may be dropped; the compiler warns with BP2016)");
        }
    }

    private void DrawConditionMetForm()
    {
        _node.ConditionMet ??= new ConditionMetPayload();
        ImGui.TextDisabled("(Condition Met form — predicate editor)");
    }

    private void DrawEqsResultForm()
    {
        _node.EqsResult ??= new EqsResultPayload();
        ImGui.TextDisabled("(EQS Result form — trigger and sensor picker)");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
