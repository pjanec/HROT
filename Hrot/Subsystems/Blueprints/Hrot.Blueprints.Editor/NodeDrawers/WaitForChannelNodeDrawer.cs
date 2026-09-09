using System.Linq;
using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// BP-06 — Details-panel editor for <see cref="WaitForChannelNode.ChannelType"/>.
///
/// <para>
/// The node runs and is run-proven, but had no drawer: the channel it waits on could never be
/// changed after placement. The channel list comes from the same
/// <see cref="IChannelCommandCatalog"/> that <see cref="ChannelCommandNodeDrawer"/> reads — the
/// distinct <c>ChannelTypeFqn</c> values across its entries.
/// </para>
///
/// <para>
/// Unlike <c>ChannelCommandNode</c>, which bakes an immutable action at creation (D-B: swapping it
/// would orphan param pins), a <c>WaitForChannel</c> node's pins do not depend on the channel — it
/// is exec-only — so the value is freely editable here.
/// </para>
/// </summary>
public sealed class WaitForChannelNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IChannelCommandCatalog _catalog;
    private readonly IEditService           _editService;

    public WaitForChannelNodeDrawer(IChannelCommandCatalog catalog, IEditService editService)
    {
        _catalog     = catalog     ?? throw new ArgumentNullException(nameof(catalog));
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    public bool Handles(Node node) => node is WaitForChannelNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new WaitForChannelNodeSession((WaitForChannelNode)node, parentAsset, _catalog, _editService);
}

/// <summary>
/// Edit session for <see cref="WaitForChannelNode"/>. Mutation and list logic live in helpers with
/// internal test hooks (mirroring <c>GetSharedNodeSession</c>); <see cref="Draw"/> is the only
/// ImGui-coupled surface.
/// </summary>
internal sealed class WaitForChannelNodeSession : INodeEditSession
{
    private readonly WaitForChannelNode     _node;
    private readonly BlueprintAsset         _parent;
    private readonly IChannelCommandCatalog _catalog;
    private readonly IEditService           _editService;

    // ImGui view-state only (the incremental filter box's current text).
    private string _filterText = "";

    public bool IsDirty { get; private set; }

    public WaitForChannelNodeSession(
        WaitForChannelNode node, BlueprintAsset parentAsset,
        IChannelCommandCatalog catalog, IEditService editService)
    {
        _node        = node;
        _parent      = parentAsset;
        _catalog     = catalog;
        _editService = editService;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>Test hook: simulates the designer picking (or otherwise setting) the channel.</summary>
    internal void SetChannelTypeForTest(string channelType) => ApplyChannelType(channelType);

    /// <summary>Test hook: the distinct channel FQNs the catalog exposes, sorted.</summary>
    internal IReadOnlyList<string> GetAvailableChannelsForTest() => AvailableChannels();

    /// <summary>Test hook: the discovered channels matching a filter (case-insensitive substring).</summary>
    internal IReadOnlyList<string> GetFilteredChannelsForTest(string filterText)
        => SharedTypePickerLogic.Filter(AvailableChannels(), filterText);

    /// <summary>
    /// Test hook: true when the node's current channel is non-empty but absent from the catalog
    /// (unloaded assembly, renamed channel). The picker must surface and preserve such a value.
    /// </summary>
    internal bool IsCurrentChannelUnlistedForTest()
        => !string.IsNullOrEmpty(_node.ChannelType)
           && !SharedTypePickerLogic.Contains(AvailableChannels(), _node.ChannelType);

    // ── Private helpers (called by both Draw() and test hooks) ─────────────────

    /// <summary>
    /// One entry per distinct channel. The catalog is keyed by (channel, action), so several
    /// entries share a channel — deduplicated here, and sorted so the list is stable between runs.
    /// </summary>
    private IReadOnlyList<string> AvailableChannels()
        => _catalog.GetEntries()
            .Select(e => e.ChannelTypeFqn)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

    private void ApplyChannelType(string channelType)
    {
        if (channelType == _node.ChannelType) return;

        var before = _node.ChannelType;
        _editService.RecordPropertyEdit(
            _parent, $"Set Wait Channel '{channelType}'",
            apply: () => { _node.ChannelType = channelType; IsDirty = true; },
            undo:  () => { _node.ChannelType = before;      IsDirty = true; });
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Wait For Channel");
        ImGui.Separator();

        DrawChannelPicker();

        ImGui.TextDisabled("(latent — resumes when the channel's action instance completes)");
        if (string.IsNullOrEmpty(_node.ChannelType))
            ImGui.TextColored(EditorColors.Warning, "(no channel selected — this node will never resume)");
    }

    /// <summary>
    /// Filtered combo over the catalog's distinct channels, mirroring
    /// <c>GetSharedNodeSession.DrawSharedTypePicker</c>. An unlisted current value is surfaced as a
    /// selectable entry so opening and closing the combo without choosing never clears it.
    /// </summary>
    private void DrawChannelPicker()
    {
        var current    = _node.ChannelType ?? "";
        var unlisted   = IsCurrentChannelUnlistedForTest();
        var comboLabel = current.Length > 0 ? current : "(none)";

        if (ImGui.BeginCombo("Channel", comboLabel))
        {
            ImGui.InputTextWithHint("##WaitForChannelFilter", "Filter...", ref _filterText, 256);

            if (unlisted)
            {
                ImGui.Selectable($"{current} (current — not in catalog)", true);
                ImGui.Separator();
            }

            foreach (var channel in GetFilteredChannelsForTest(_filterText))
            {
                bool selected = channel == current;
                if (ImGui.Selectable(channel, selected))
                    ApplyChannelType(channel);
                if (selected) ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (unlisted)
            ImGui.TextColored(EditorColors.Warning,
                $"(current channel not in the catalog — kept as-is: {current})");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
