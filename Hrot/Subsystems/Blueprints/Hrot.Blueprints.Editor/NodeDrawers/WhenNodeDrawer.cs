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

    private void DrawEventFiredForm()
    {
        _node.EventFired ??= new EventFiredPayload();
        var entries = _eventCatalog.GetEntries();
        ImGui.TextDisabled($"(Event Fired form — {entries.Count} events available)");
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
