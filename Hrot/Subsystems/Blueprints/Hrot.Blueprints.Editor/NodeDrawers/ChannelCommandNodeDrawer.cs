using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Node-drawer for <see cref="ChannelCommandNode"/>.
/// Lets the designer configure the command by selecting a channel action from the catalog
/// (sets <see cref="ChannelCommandNode.ChannelType"/> and <see cref="ChannelCommandNode.ActionId"/>).
/// Once configured, <see cref="NodePinSchema"/> projects the matching parameter data-IN pins and
/// the node title updates to <c>"Command: {ActionId}"</c>.
/// </summary>
public sealed class ChannelCommandNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;
    private readonly IChannelCommandCatalog _catalog;

    public ChannelCommandNodeDrawer(IChannelCommandCatalog catalog, IEditService editService)
    {
        _catalog     = catalog     ?? throw new ArgumentNullException(nameof(catalog));
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    public bool Handles(Node node) => node is ChannelCommandNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new ChannelCommandNodeSession((ChannelCommandNode)node, parentAsset, _catalog, _editService);
}

internal sealed class ChannelCommandNodeSession : INodeEditSession
{
    private readonly ChannelCommandNode _node;
    private readonly BlueprintAsset _parent;
    private readonly IChannelCommandCatalog _catalog;
    private readonly IEditService _editService;

    public bool IsDirty { get; private set; }

    public ChannelCommandNodeSession(
        ChannelCommandNode node,
        BlueprintAsset parentAsset,
        IChannelCommandCatalog catalog,
        IEditService editService)
    {
        _node        = node;
        _parent      = parentAsset;
        _catalog     = catalog;
        _editService = editService;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>
    /// Test hook: simulates the designer selecting a channel action by index in the catalog.
    /// Sets ChannelType and ActionId, and marks session dirty.
    /// </summary>
    internal void SelectActionForTest(int catalogIndex)
    {
        var entries = _catalog.GetEntries();
        if (catalogIndex < 0 || catalogIndex >= entries.Count) return;
        ApplySelection(entries[catalogIndex]);
    }

    // ── Private mutation helpers ─────────────────────────────────────────────────

    private void ApplySelection(ChannelCommandCatalogEntry entry)
    {
        // ChannelType is stored as the SHORT class name (e.g. "LocomotionChannel"), not the FQN.
        // NodePinSchema.ChannelCommandPins and Stage2_Validate.V_ChannelCommandReferences both
        // match via LastSegment(ChannelTypeFqn) == node.ChannelType. (See NodePinSchema line ~528.)
        _node.ChannelType = LastSegment(entry.ChannelTypeFqn);
        _node.ActionId    = entry.Name;
        MarkChanged();
    }

    private static string LastSegment(string fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return fqn;
        var idx = fqn.LastIndexOf('.');
        return idx >= 0 ? fqn[(idx + 1)..] : fqn;
    }

    private void MarkChanged()
    {
        IsDirty = true;
        _editService?.MarkDirty(_parent);
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Channel Command");
        ImGui.Separator();

        var entries = _catalog.GetEntries();
        if (entries.Count == 0)
        {
            ImGui.TextColored(EditorColors.Warning, "(no channel command catalog entries)");
            return;
        }

        // Build display labels: "{ChannelType} / {ActionId}" (short channel type + action name).
        var labels = new string[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            // Use the short class name of the channel type FQN for readability.
            var fqn       = entries[i].ChannelTypeFqn ?? "";
            var dot       = fqn.LastIndexOf('.');
            var shortType = dot >= 0 ? fqn[(dot + 1)..] : fqn;
            labels[i] = $"{shortType} / {entries[i].Name}";
        }

        // Find current selection index.
        // ChannelType is stored as the SHORT class name matching LastSegment(ChannelTypeFqn).
        int currentIdx = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            if (LastSegment(entries[i].ChannelTypeFqn) == _node.ChannelType &&
                entries[i].Name                        == _node.ActionId)
            {
                currentIdx = i;
                break;
            }
        }

        if (ImGui.Combo("Action", ref currentIdx, labels, labels.Length))
        {
            if (currentIdx >= 0)
            {
                var chosen = entries[currentIdx];
                if (chosen.ChannelTypeFqn != _node.ChannelType || chosen.Name != _node.ActionId)
                {
                    ApplySelection(chosen);
                }
            }
        }

        if (currentIdx < 0)
            ImGui.TextColored(EditorColors.Warning, "(no action selected — param pins hidden)");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
