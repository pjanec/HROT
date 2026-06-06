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
        // AN7: dispatch on whether this is a non-channel action (ActionFqn set) or a channel
        // command (existing ChannelType/ActionId path).
        if (!string.IsNullOrEmpty(_node.ActionFqn))
        {
            DrawNonChannelAction();
            return;
        }

        DrawChannelCommand();
    }

    /// <summary>
    /// AN7 — non-channel action display (ActionFqn set).
    /// Shows the action identity read-only (D-B: action is immutable after creation).
    /// No mutation path: action selection is create-time-only via the per-action palette.
    /// </summary>
    private void DrawNonChannelAction()
    {
        ImGui.Text("Behavior Action");
        ImGui.Separator();

        // AN5/AN7 (D-B): action is baked at creation — render as read-only label only.
        // Extract the short method name for a friendlier display (last segment of FQN).
        var fqn         = _node.ActionFqn!;
        var dotIdx      = fqn.LastIndexOf('.');
        var shortName   = dotIdx >= 0 ? fqn[(dotIdx + 1)..] : fqn;
        var typePortion = dotIdx > 0  ? fqn[..dotIdx]       : "";

        ImGui.LabelText("Action", shortName);
        if (!string.IsNullOrEmpty(typePortion))
            ImGui.LabelText("Type",   typePortion);
        ImGui.LabelText("FQN",    fqn);

        ImGui.Spacing();
        ImGui.TextDisabled("(compile lowering via AN8 — not yet emittable)");
    }

    /// <summary>
    /// Existing channel-command display (ActionFqn null/empty).
    /// AN5 (D-B): action is baked at creation — render as read-only labels.
    /// </summary>
    private void DrawChannelCommand()
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
