using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// ⭐ <c>CE-2083</c> (<c>docs/DESIGN_Decision_Layer.md</c> §4.10 D4/D5) — the Details editor of an SOP order: which behaviour it
/// starts (the host's REGISTERED names, every tier — the BTree SOP order's picker), the order (Do when idle / React) and, for
/// React, the urgency. Picking a behaviour bakes its AUTHORED params type, so the node shows a typed <c>Params</c> pin.
/// ⚠ A name not in the list is kept and flagged, never dropped (a behaviour may register later).
/// </summary>
public sealed class SopOrderNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;
    private readonly Func<IReadOnlyList<string>> _behaviourNames;
    private readonly Func<string, string?> _paramsTypeOf;

    /// <param name="behaviourNames">The host's registered behaviour names. ⛔ A production host HAS a <c>BehaviorRegistry</c>
    /// and must pass it (the silent-default rule); null lists nothing.</param>
    /// <param name="paramsTypeOf">A behaviour's AUTHORED params type id (<c>ChildInputTypes.ParamsDtoLookup</c> — what an SOP
    /// order serialises, not the hosted-input type). ⛔ A production host must pass it too; null = no pin.</param>
    public SopOrderNodeDrawer(IEditService editService, Func<IReadOnlyList<string>>? behaviourNames = null,
                              Func<string, string?>? paramsTypeOf = null)
    {
        _editService    = editService ?? throw new ArgumentNullException(nameof(editService));
        _behaviourNames = behaviourNames ?? (() => Array.Empty<string>());
        _paramsTypeOf   = paramsTypeOf ?? (_ => null);
    }

    public bool Handles(Node node) => node is SopOrderNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new SopOrderNodeSession((SopOrderNode)node, parentAsset, _editService, _behaviourNames, _paramsTypeOf);
}

internal sealed class SopOrderNodeSession : INodeEditSession
{
    private static readonly string[] Kinds = { "Do when idle", "React" };
    private static readonly string[] Urgencies = Enum.GetNames(typeof(SopOrderUrgency)).Skip(1).ToArray();   // not NotAReaction

    private readonly SopOrderNode _node;
    private readonly BlueprintAsset _parent;
    private readonly IEditService _editService;
    private readonly Func<IReadOnlyList<string>> _names;
    private readonly Func<string, string?> _paramsTypeOf;
    private string _filter = "";

    public bool IsDirty { get; private set; }

    public SopOrderNodeSession(SopOrderNode node, BlueprintAsset parent, IEditService editService,
        Func<IReadOnlyList<string>> names, Func<string, string?> paramsTypeOf)
    {
        _node = node; _parent = parent; _editService = editService; _names = names; _paramsTypeOf = paramsTypeOf;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────
    internal void SetBehaviourForTest(string name) => ApplyBehaviour(name);

    private IReadOnlyList<string> Filtered(string filter)
        => SharedTypePickerLogic.Filter(
            _names().Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal)
                    .OrderBy(n => n, StringComparer.Ordinal).ToList(),
            filter);

    private bool IsUnlisted()
        => !string.IsNullOrWhiteSpace(_node.BehaviorName) && !_names().Contains(_node.BehaviorName, StringComparer.Ordinal);

    /// <summary>One undoable edit: the behaviour's name AND its params type, which projects the <c>Params</c> pin — the pins
    /// are re-derived and a wire to a vanished pin is pruned (restored on undo), the BP-202 rule (as the Behaviour Task).</summary>
    private void ApplyBehaviour(string name)
    {
        name = name.Trim();
        if (name == _node.BehaviorName) return;
        var (beforeName, beforeType) = (_node.BehaviorName, _node.ParamsTypeId);
        var afterType = _paramsTypeOf(name);
        var graph = DerivedPinMaintenance.FindOwningGraph(_parent, _node);
        List<Link>? prunedForward = null, prunedBack = null;

        void Set(string n, string? t, List<Link>? restore, ref List<Link>? pruned)
        {
            var validBefore = graph != null ? DerivedPinMaintenance.PinIds(_node) : null;
            _node.BehaviorName = n;
            _node.ParamsTypeId = t;
            DerivedPinMaintenance.ResyncPins(_node);
            if (graph != null)
            {
                DerivedPinMaintenance.Restore(graph, restore);
                pruned = DerivedPinMaintenance.PruneVanished(graph, _node, validBefore!);
            }
            IsDirty = true;
            _editService.NotifyStructureChanged(_parent);
        }

        _editService.RecordPropertyEdit(_parent, $"Set SOP order behaviour '{name}'",
            apply: () => Set(name, afterType, prunedBack, ref prunedForward),
            undo:  () => Set(beforeName, beforeType, prunedForward, ref prunedBack));
    }

    private void ApplyKind(SopOrderKind kind)
    {
        var before = _node.Kind;
        if (kind == before) return;
        _editService.RecordPropertyEdit(_parent, $"Set SOP order '{kind}'",
            apply: () => { _node.Kind = kind;   IsDirty = true; _editService.NotifyStructureChanged(_parent); },
            undo:  () => { _node.Kind = before; IsDirty = true; _editService.NotifyStructureChanged(_parent); });
    }

    private void ApplyUrgency(SopOrderUrgency urgency)
    {
        var before = _node.Urgency;
        if (urgency == before) return;
        _editService.RecordPropertyEdit(_parent, $"Set SOP urgency '{urgency}'",
            apply: () => { _node.Urgency = urgency; IsDirty = true; _editService.NotifyStructureChanged(_parent); },
            undo:  () => { _node.Urgency = before;  IsDirty = true; _editService.NotifyStructureChanged(_parent); });
    }

    public void Draw()
    {
        ImGui.Text("SOP order");
        ImGui.Separator();

        int kind = (int)_node.Kind;
        if (ImGui.Combo("Order", ref kind, Kinds, Kinds.Length)) ApplyKind((SopOrderKind)kind);

        var current = string.IsNullOrWhiteSpace(_node.BehaviorName) ? "(none)" : _node.BehaviorName;
        if (ImGui.BeginCombo("Behaviour", current))
        {
            ImGui.InputTextWithHint("##SopOrderFilter", "Filter...", ref _filter, 256);
            foreach (var name in Filtered(_filter))
            {
                bool selected = name == _node.BehaviorName;
                if (ImGui.Selectable(name, selected)) ApplyBehaviour(name);
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (_node.Kind == SopOrderKind.React)
        {
            int u = Math.Max(0, Array.IndexOf(Urgencies, _node.Urgency.ToString()));
            if (ImGui.Combo("Urgency", ref u, Urgencies, Urgencies.Length))
                ApplyUrgency(Enum.Parse<SopOrderUrgency>(Urgencies[u]));
        }

        if (string.IsNullOrWhiteSpace(_node.BehaviorName))
            ImGui.TextColored(EditorColors.Warning, "(no behaviour picked — this order does not compile, BP1688)");
        else if (IsUnlisted())
            ImGui.TextColored(EditorColors.Warning, $"(not registered on this host now — kept: {_node.BehaviorName})");

        if (!string.IsNullOrWhiteSpace(_node.ParamsTypeId))
            ImGui.TextDisabled($"Params: {_node.ParamsTypeId} — unwired sends the behaviour's defaults.");
        ImGui.TextDisabled("Accepted is false when the gate refused the order (Branch on it to try the next row).");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
