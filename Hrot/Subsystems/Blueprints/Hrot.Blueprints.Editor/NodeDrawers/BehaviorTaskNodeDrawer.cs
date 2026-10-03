using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// ⭐ S7a (<c>DESIGN_Unified_Behaviour_Run</c> "S7 design" D8) — the Details editor of a Behaviour Task: which behaviour it
/// runs. The picker lists the host's REGISTERED behaviour names (every tier: BTree, HSM, blueprint, hand-written) — the
/// names the runtime resolves the child by (<c>HostedChildren</c>). ⚠ A name not in the list is kept and flagged, never
/// dropped (a child may register later).
/// </summary>
public sealed class BehaviorTaskNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;
    private readonly Func<IReadOnlyList<string>> _behaviourNames;
    private readonly Func<string, string?> _paramsTypeOf;

    /// <param name="behaviourNames">The host's registered behaviour names. ⛔ A production host HAS a
    /// <c>BehaviorRegistry</c> and must pass it (the silent-default rule); null lists nothing.</param>
    /// <param name="paramsTypeOf">⭐ S8 / CE-2022 — a child's hosted input type id (<see cref="ParamsTypeLookup"/>), baked
    /// on pick so the node shows a typed <c>Params</c> pin. ⛔ A production host must pass it too; null = no pin.</param>
    public BehaviorTaskNodeDrawer(IEditService editService, Func<IReadOnlyList<string>>? behaviourNames = null,
                                  Func<string, string?>? paramsTypeOf = null)
    {
        _editService    = editService ?? throw new ArgumentNullException(nameof(editService));
        _behaviourNames = behaviourNames ?? (() => Array.Empty<string>());
        _paramsTypeOf   = paramsTypeOf ?? (_ => null);
    }

    public bool Handles(Node node) => node is RunBehaviorNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new BehaviorTaskNodeSession((RunBehaviorNode)node, parentAsset, _editService, _behaviourNames, _paramsTypeOf);

    /// <summary>
    /// ⭐ S8 / <c>CE-2022</c> — the ONE lookup both hosts pass: <c>BehaviorRegistry.TryGetHostedInputType</c>, spelled as a
    /// blueprint type id (a nested type's <c>+</c> becomes <c>.</c>, as C# writes it).
    /// </summary>
    public static Func<string, string?> ParamsTypeLookup(Func<Fdp.Toolkit.Behavior.BehaviorRegistry?> registry)
        => name => registry() is { } r && r.TryGetHostedInputType(name, out var type) && type.FullName is { } full
            ? full.Replace('+', '.')
            : null;
}

internal sealed class BehaviorTaskNodeSession : INodeEditSession
{
    private readonly RunBehaviorNode _node;
    private readonly BlueprintAsset _parent;
    private readonly IEditService _editService;
    private readonly Func<IReadOnlyList<string>> _names;
    private readonly Func<string, string?> _paramsTypeOf;
    private string _filter = "";

    public bool IsDirty { get; private set; }

    public BehaviorTaskNodeSession(RunBehaviorNode node, BlueprintAsset parent, IEditService editService,
        Func<IReadOnlyList<string>> names, Func<string, string?> paramsTypeOf)
    {
        _node = node; _parent = parent; _editService = editService; _names = names; _paramsTypeOf = paramsTypeOf;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────
    internal void SetBehaviourForTest(string name) => ApplyBehaviour(name);
    internal IReadOnlyList<string> GetFilteredBehavioursForTest(string filter) => Filtered(filter);
    internal bool IsCurrentUnlistedForTest() => IsUnlisted();

    private IReadOnlyList<string> Filtered(string filter)
        => SharedTypePickerLogic.Filter(
            _names().Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal)
                    .OrderBy(n => n, StringComparer.Ordinal).ToList(),
            filter);

    private bool IsUnlisted()
        => !string.IsNullOrWhiteSpace(_node.BehaviorName) && !_names().Contains(_node.BehaviorName, StringComparer.Ordinal);

    /// <summary>
    /// One undoable edit: the child's name AND (S8) its parameter type, which projects the <c>Params</c> pin. ⚠ The pin set
    /// changes, so the stored pins are re-derived and a wire to a pin that vanished is pruned (and restored on undo) —
    /// the BP-202 rule every pin-deriving drawer follows.
    /// </summary>
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

        _editService.RecordPropertyEdit(_parent, $"Set Behaviour Task '{name}'",
            apply: () => Set(name, afterType, prunedBack, ref prunedForward),
            undo:  () => Set(beforeName, beforeType, prunedForward, ref prunedBack));
    }

    public void Draw()
    {
        ImGui.Text("Behaviour Task");
        ImGui.Separator();

        var current = string.IsNullOrWhiteSpace(_node.BehaviorName) ? "(none)" : _node.BehaviorName;
        if (ImGui.BeginCombo("Behaviour", current))
        {
            ImGui.InputTextWithHint("##BehaviourTaskFilter", "Filter...", ref _filter, 256);
            foreach (var name in Filtered(_filter))
            {
                bool selected = name == _node.BehaviorName;
                if (ImGui.Selectable(name, selected)) ApplyBehaviour(name);
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (string.IsNullOrWhiteSpace(_node.BehaviorName))
            ImGui.TextColored(EditorColors.Warning, "(no behaviour picked — this task does not compile, BP1659)");
        else if (IsUnlisted())
            ImGui.TextColored(EditorColors.Warning, $"(not registered on this host now — kept: {_node.BehaviorName})");

        if (!string.IsNullOrWhiteSpace(_node.ParamsTypeId))
            ImGui.TextDisabled($"Params: {_node.ParamsTypeId} — wire it to start the child with these values.");
        else if (!string.IsNullOrWhiteSpace(_node.BehaviorName))
            ImGui.TextDisabled("(this behaviour takes no parameters from a host — it starts from its defaults)");

        ImGui.TextDisabled("Start runs it and waits: Succeeded / Failed when it ends, While Running each frame.");
        ImGui.TextDisabled("Wire Started to run it alongside (a second Start restarts it).");
        ImGui.TextDisabled("Abort (from While Running, or after Started) stops it and continues on Failed.");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
