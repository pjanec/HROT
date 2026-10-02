using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// ⭐ CE-2015 (<c>DESIGN_Typed_Event_Nodes</c> E4, I8) — the Details editor of an event node. A TYPED node ("On: X") is
/// one handler, so it carries its own arrival policy (<c>DESIGN_Unified_Behaviour_Run</c> U-6: Parallel / Restart /
/// Queue + Capacity) and, when its event names a recipient, the Self/Any filter. ⛔ Its payload is the event's baked
/// shape (from the palette) and is not edited here. An untyped entry (a Function / Macro / custom-event body) has
/// nothing of its own to edit.
/// </summary>
public sealed class EventEntryNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;

    public EventEntryNodeDrawer(IEditService editService)
        => _editService = editService ?? throw new ArgumentNullException(nameof(editService));

    public bool Handles(Node node) => node is EventEntryNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new EventEntryNodeSession((EventEntryNode)node, parentAsset, _editService);
}

internal sealed class EventEntryNodeSession : INodeEditSession
{
    private static readonly EventFiberPolicy[] Policies =
        { EventFiberPolicy.Parallel, EventFiberPolicy.Restart, EventFiberPolicy.Queue };

    private readonly EventEntryNode _node;
    private readonly BlueprintAsset _parent;
    private readonly IEditService   _editService;

    public bool IsDirty { get; private set; }

    public EventEntryNodeSession(EventEntryNode node, BlueprintAsset parentAsset, IEditService editService)
    {
        _node        = node;
        _parent      = parentAsset;
        _editService = editService;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────
    internal void SetPolicyForTest(EventFiberPolicy policy) => ApplyPolicy(policy);
    internal void SetCapacityForTest(int capacity) => ApplyCapacity(capacity);
    internal void SetSelfFilterForTest(bool self) => ApplySelfFilter(self);

    /// <summary>Restart runs one handler (the newest wins), so it holds no Capacity above 1 (BP1681).</summary>
    private void ApplyPolicy(EventFiberPolicy policy)
    {
        if (policy == _node.Policy) return;
        var (beforePolicy, beforeCapacity) = (_node.Policy, _node.Capacity);
        int capacity = policy == EventFiberPolicy.Restart ? 0 : _node.Capacity;
        _editService.RecordPropertyEdit(_parent, $"Set Event Policy '{policy}'",
            apply: () => { _node.Policy = policy;       _node.Capacity = capacity;       IsDirty = true; },
            undo:  () => { _node.Policy = beforePolicy; _node.Capacity = beforeCapacity; IsDirty = true; });
    }

    /// <summary>Clamped to 1..16 (BP1681's range); 1 is stored as 0 (the default, omitted from JSON).</summary>
    private void ApplyCapacity(int capacity)
    {
        int stored = Math.Clamp(capacity, 1, EventEntryNode.MaxCapacity);
        if (stored == 1) stored = 0;
        if (stored == _node.Capacity) return;
        var before = _node.Capacity;
        _editService.RecordPropertyEdit(_parent, $"Set Event Capacity {Math.Max(1, stored)}",
            apply: () => { _node.Capacity = stored; IsDirty = true; },
            undo:  () => { _node.Capacity = before; IsDirty = true; });
    }

    private void ApplySelfFilter(bool self)
    {
        if (self == _node.TargetFilterSelf) return;
        var before = _node.TargetFilterSelf;
        _editService.RecordPropertyEdit(_parent, self ? "Event: Self only" : "Event: Any recipient",
            apply: () => { _node.TargetFilterSelf = self;   IsDirty = true; },
            undo:  () => { _node.TargetFilterSelf = before; IsDirty = true; });
    }

    public void Draw()
    {
        if (!EventPayload.IsTyped(_node))
        {
            ImGui.Text("Entry");
            ImGui.Separator();
            ImGui.TextDisabled("The entry of this graph — its inputs are the graph's signature.");
            return;
        }

        ImGui.Text($"On: {_node.EventTypeId}");
        ImGui.Separator();

        int current = Array.IndexOf(Policies, _node.Policy);
        if (ImGui.BeginCombo("Policy", _node.Policy.ToString()))
        {
            for (int i = 0; i < Policies.Length; i++)
                if (ImGui.Selectable(Policies[i].ToString(), i == current))
                    ApplyPolicy(Policies[i]);
            ImGui.EndCombo();
        }
        ImGui.TextDisabled(_node.Policy switch
        {
            EventFiberPolicy.Restart => "(an arrival while the handler waits restarts it — the newest wins)",
            EventFiberPolicy.Queue   => "(arrivals wait in line and run one after another)",
            _                        => "(each arrival runs its own handler, up to Capacity at once)",
        });

        if (_node.Policy != EventFiberPolicy.Restart)
        {
            int capacity = Math.Max(1, _node.Capacity);
            if (ImGui.InputInt("Capacity", ref capacity))
                ApplyCapacity(capacity);
        }

        if (!string.IsNullOrEmpty(_node.TargetFieldName))
        {
            bool self = _node.TargetFilterSelf;
            if (ImGui.Checkbox($"Only when '{_node.TargetFieldName}' is this entity", ref self))
                ApplySelfFilter(self);
        }
        ImGui.TextDisabled("(waiting handlers apply to behaviours; an Instance handler runs to the end at once)");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
