using System.Linq;
using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// BP-08 — Details-panel editor for <see cref="CallPeerBlueprintNode"/>'s target: which peer
/// Blueprint, and which of its exported functions.
///
/// <para>
/// Two dependent pickers. Choosing a peer narrows the function list to that peer's exports, and
/// clears a function that the new peer does not export — leaving a stale <c>FunctionRef</c> would
/// silently collapse the node's pins back to the untyped <c>exec + Return:System.Object</c> fallback
/// (<c>NodePinSchema.CallPeerBlueprintPins</c>) with nothing on screen to say why.
/// </para>
///
/// <para>
/// Both fields drive pin projection, so every edit here is structural.
/// </para>
/// </summary>
public sealed class CallPeerBlueprintNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IBlueprintPeerProvider _peers;
    private readonly IEditService           _editService;

    public CallPeerBlueprintNodeDrawer(IEditService editService, IBlueprintPeerProvider? peers = null)
    {
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
        _peers       = peers ?? EmptyBlueprintPeerProvider.Instance;
    }

    public bool Handles(Node node) => node is CallPeerBlueprintNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new CallPeerBlueprintNodeSession((CallPeerBlueprintNode)node, parentAsset, _peers, _editService);
}

/// <summary>
/// Edit session for <see cref="CallPeerBlueprintNode"/>. Mutation and list logic live in helpers
/// with internal test hooks; <see cref="Draw"/> is the only ImGui-coupled surface.
/// </summary>
internal sealed class CallPeerBlueprintNodeSession : INodeEditSession
{
    private readonly CallPeerBlueprintNode  _node;
    private readonly BlueprintAsset         _parent;
    private readonly IBlueprintPeerProvider _peers;
    private readonly IEditService           _editService;

    // ImGui view-state only (the incremental filter box's current text).
    private string _peerFilterText = "";

    public bool IsDirty { get; private set; }

    public CallPeerBlueprintNodeSession(
        CallPeerBlueprintNode node, BlueprintAsset parentAsset,
        IBlueprintPeerProvider peers, IEditService editService)
    {
        _node        = node;
        _parent      = parentAsset;
        _peers       = peers;
        _editService = editService;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>Test hook: simulates the designer picking a peer Blueprint.</summary>
    internal void SetPeerForTest(Guid assetId) => ApplyPeer(assetId);

    /// <summary>Test hook: simulates the designer picking one of the peer's exported functions.</summary>
    internal void SetFunctionForTest(string functionRef) => ApplyFunction(functionRef);

    /// <summary>Test hook: every discoverable peer.</summary>
    internal IReadOnlyList<BlueprintPeerInfo> GetAvailablePeersForTest() => _peers.GetPeers();

    /// <summary>Test hook: peers matching a filter, over both display name and asset id.</summary>
    internal IReadOnlyList<BlueprintPeerInfo> GetFilteredPeersForTest(string filterText)
    {
        var all = _peers.GetPeers();
        if (string.IsNullOrEmpty(filterText)) return all;
        return all.Where(p =>
                p.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase)
                || p.AssetId.ToString().Contains(filterText, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Test hook: the exported functions of the currently-selected peer (empty when unresolved).</summary>
    internal IReadOnlyList<string> GetFunctionsForCurrentPeerForTest()
        => ResolveCurrentPeer()?.ExportedFunctions ?? Array.Empty<string>();

    /// <summary>
    /// Test hook: true when <see cref="CallPeerBlueprintNode.PeerBlueprintId"/> is set but matches no
    /// discovered peer — a dangling reference the picker must surface, not silently blank.
    /// </summary>
    internal bool IsCurrentPeerUnresolvedForTest()
        => !string.IsNullOrEmpty(_node.PeerBlueprintId) && ResolveCurrentPeer() is null;

    /// <summary>
    /// Test hook: true when a function is named but the resolved peer does not export it. Distinct
    /// from an unresolved peer: here we know the peer and know the name is wrong.
    /// </summary>
    internal bool IsCurrentFunctionUnresolvedForTest()
    {
        if (string.IsNullOrEmpty(_node.FunctionRef)) return false;
        var peer = ResolveCurrentPeer();
        if (peer is null) return false;
        return !peer.ExportedFunctions.Contains(_node.FunctionRef, StringComparer.Ordinal);
    }

    // ── Private helpers (called by both Draw() and test hooks) ─────────────────

    private BlueprintPeerInfo? ResolveCurrentPeer()
    {
        if (!Guid.TryParse(_node.PeerBlueprintId, out var id)) return null;
        return _peers.GetPeers().FirstOrDefault(p => p.AssetId == id);
    }

    private static string PeerLabel(BlueprintPeerInfo peer)
        => string.IsNullOrEmpty(peer.Name) ? peer.AssetId.ToString("D") : peer.Name;

    /// <summary>
    /// Selects a peer. A <c>FunctionRef</c> the new peer does not export is cleared in the same
    /// undoable edit — one gesture, one entry, and no half-valid intermediate state to undo through.
    /// </summary>
    private void ApplyPeer(Guid assetId)
    {
        var newId = assetId.ToString("D");
        if (newId == _node.PeerBlueprintId) return;

        var beforePeer = _node.PeerBlueprintId;
        var beforeFunc = _node.FunctionRef;

        var peer = _peers.GetPeers().FirstOrDefault(p => p.AssetId == assetId);
        var keepFunction = peer is not null
            && !string.IsNullOrEmpty(beforeFunc)
            && peer.ExportedFunctions.Contains(beforeFunc, StringComparer.Ordinal);
        var afterFunc = keepFunction ? beforeFunc : "";

        _editService.RecordPropertyEdit(
            _parent, $"Set Peer Blueprint '{(peer is null ? newId : PeerLabel(peer))}'",
            apply: () =>
            {
                _node.PeerBlueprintId = newId;
                _node.FunctionRef     = afterFunc;
                AfterChange();
            },
            undo: () =>
            {
                _node.PeerBlueprintId = beforePeer;
                _node.FunctionRef     = beforeFunc;
                AfterChange();
            });
    }

    private void ApplyFunction(string functionRef)
    {
        if (functionRef == _node.FunctionRef) return;

        var before = _node.FunctionRef;
        _editService.RecordPropertyEdit(
            _parent, $"Set Peer Function '{functionRef}'",
            apply: () => { _node.FunctionRef = functionRef; AfterChange(); },
            undo:  () => { _node.FunctionRef = before;      AfterChange(); });
    }

    /// <summary>
    /// Both fields feed <c>NodePinSchema.CallPeerBlueprintPins</c>, which types the node's argument
    /// and return pins from the peer's signature — so every edit here is structural.
    /// </summary>
    private void AfterChange()
    {
        IsDirty = true;
        _editService.NotifyStructureChanged(_parent);
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Call Peer Blueprint");
        ImGui.Separator();

        DrawPeerPicker();
        DrawFunctionPicker();

        if (string.IsNullOrEmpty(_node.PeerBlueprintId) || string.IsNullOrEmpty(_node.FunctionRef))
            ImGui.TextColored(EditorColors.Warning,
                "(both a peer and a function are required — untyped exec+Return pins until then)");
    }

    private void DrawPeerPicker()
    {
        var all        = _peers.GetPeers();
        var current    = ResolveCurrentPeer();
        var unresolved = IsCurrentPeerUnresolvedForTest();

        if (all.Count == 0 && !unresolved)
        {
            ImGui.TextColored(EditorColors.Warning,
                "(no peer Blueprints discovered — check the blueprint asset root)");
            return;
        }

        var comboLabel = current is not null ? PeerLabel(current)
                       : unresolved          ? $"{_node.PeerBlueprintId} (unresolved)"
                       : "(none)";

        if (ImGui.BeginCombo("Peer Blueprint", comboLabel))
        {
            ImGui.InputTextWithHint("##CallPeerFilter", "Filter...", ref _peerFilterText, 256);

            if (unresolved)
            {
                ImGui.Selectable($"{_node.PeerBlueprintId} (current — not discovered)", true);
                ImGui.Separator();
            }

            foreach (var peer in GetFilteredPeersForTest(_peerFilterText))
            {
                bool selected = current is not null && peer.AssetId == current.AssetId;
                if (ImGui.Selectable(PeerLabel(peer), selected))
                    ApplyPeer(peer.AssetId);
                if (selected) ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (unresolved)
            ImGui.TextColored(EditorColors.Warning,
                $"(no discovered Blueprint has id {_node.PeerBlueprintId} — kept as-is)");
    }

    private void DrawFunctionPicker()
    {
        var peer = ResolveCurrentPeer();

        // Nothing to choose from until the peer resolves — disable rather than hide, so the field's
        // absence is never mistaken for the node not having one.
        if (peer is null)
        {
            ImGui.BeginDisabled();
            ImGui.LabelText("Function", string.IsNullOrEmpty(_node.FunctionRef) ? "(select a peer first)" : _node.FunctionRef);
            ImGui.EndDisabled();
            return;
        }

        if (peer.ExportedFunctions.Count == 0)
        {
            ImGui.TextColored(EditorColors.Warning,
                $"({PeerLabel(peer)} exports no functions — nothing to call)");
            return;
        }

        var current    = _node.FunctionRef ?? "";
        var unresolved = IsCurrentFunctionUnresolvedForTest();
        var comboLabel = current.Length > 0 ? current : "(none)";

        if (ImGui.BeginCombo("Function", comboLabel))
        {
            if (unresolved)
            {
                ImGui.Selectable($"{current} (current — not exported)", true);
                ImGui.Separator();
            }

            foreach (var fn in peer.ExportedFunctions)
            {
                bool selected = fn == current;
                if (ImGui.Selectable(fn, selected))
                    ApplyFunction(fn);
                if (selected) ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (unresolved)
            ImGui.TextColored(EditorColors.Warning,
                $"({PeerLabel(peer)} does not export '{current}' — kept as-is; pins fall back to untyped)");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
