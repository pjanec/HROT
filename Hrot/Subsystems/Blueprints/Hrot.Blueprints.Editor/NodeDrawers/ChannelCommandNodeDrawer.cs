using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Node-drawer for <see cref="ChannelCommandNode"/>.
/// Displays the node's baked <see cref="ChannelCommandNode.ChannelType"/> and
/// <see cref="ChannelCommandNode.ActionId"/> as <b>read-only labels</b> (D-B: action is
/// immutable after creation; selection happens via the per-action palette at create-time).
/// Once configured, <see cref="NodePinSchema"/> projects the matching parameter data-IN pins and
/// the node title updates to <c>"Command: {ActionId}"</c>.
/// </summary>
public sealed class ChannelCommandNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IChannelCommandCatalog _catalog;

    public ChannelCommandNodeDrawer(IChannelCommandCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public bool Handles(Node node) => node is ChannelCommandNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new ChannelCommandNodeSession((ChannelCommandNode)node, _catalog);
}

internal sealed class ChannelCommandNodeSession : INodeEditSession
{
    private readonly ChannelCommandNode _node;
    private readonly IChannelCommandCatalog _catalog;

    /// <summary>Always false — this session is read-only; no mutations are possible.</summary>
    public bool IsDirty => false;

    public ChannelCommandNodeSession(ChannelCommandNode node, IChannelCommandCatalog catalog)
    {
        _node    = node;
        _catalog = catalog;
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Channel Command");
        ImGui.Separator();

        // AN5 (D-B): action is baked at creation — render as read-only labels.
        // No editable Combo; swapping the action after wiring would orphan param pins.
        if (string.IsNullOrEmpty(_node.ChannelType) && string.IsNullOrEmpty(_node.ActionId))
        {
            ImGui.TextDisabled("(unconfigured — drop from the per-action palette)");
            return;
        }

        ImGui.LabelText("Channel", string.IsNullOrEmpty(_node.ChannelType) ? "(none)" : _node.ChannelType);
        ImGui.LabelText("Action",  string.IsNullOrEmpty(_node.ActionId)    ? "(none)" : _node.ActionId);
    }

    public void ResetDirty() { }
    public void Dispose() { }
}
