using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Registers BTree-backed <see cref="IPickerSource{TItem}"/> implementations
/// into the picker registry.
///
/// <para>
/// Registered sources:
/// <list type="bullet">
///   <item><c>nodes.all</c> — all node kinds from <see cref="BTreeNodeCatalog"/>.</item>
///   <item><c>nodes.by-pin</c> — node kinds filtered by dragged pin compatibility.</item>
/// </list>
/// </para>
/// </summary>
public static class BTreePickerSources
{
    /// <summary>
    /// Register all BTree picker sources into <paramref name="registry"/>.
    /// Safe to call multiple times on the same registry instance — later registrations
    /// overwrite earlier ones.
    /// </summary>
    public static void Register(
        IPickerRegistry    registry,
        BTreeNodeCatalog   catalog)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (catalog  is null) throw new ArgumentNullException(nameof(catalog));

        var nodePicker = new BTreeNodePickerSource(catalog);
        registry.Register("nodes.all",   nodePicker);
        registry.Register("nodes.by-pin", nodePicker);
    }

    // ── nodes.all / nodes.by-pin ─────────────────────────────────────────────

    /// <summary>
    /// Node picker backed by <see cref="BTreeNodeCatalog"/>.
    /// When the context carries <c>sourcePinId</c>, <c>sourceDirection</c>,
    /// <c>sourceKind</c> and optionally <c>sourceType</c>, the results are
    /// filtered via <see cref="BTreeNodeCatalog.QueryForPinContext"/> — mirroring
    /// <c>BlueprintNodePickerSource</c> from the Blueprint editor.
    /// </summary>
    internal sealed class BTreeNodePickerSource : IPickerSource<NodeCatalogEntry>
    {
        private readonly BTreeNodeCatalog _catalog;

        public BTreeNodePickerSource(BTreeNodeCatalog catalog)
            => _catalog = catalog;

        public string Title             => "Add Node";
        public string EmptyResultText   => "No matching nodes.";
        public PickerLayout PreferredLayout => PickerLayout.Wide;
        public PickerSelectionMode SelectionMode => PickerSelectionMode.Single;
        public QueryCost Cost           => QueryCost.Cheap;
        public bool IsAsync             => false;
        public bool AllowsDragOut       => false;
        public bool AllowsDragIn        => false;
        public bool AllowArbitraryTextInput => false;

        public IReadOnlyList<NodeCatalogEntry> Query(
            string text,
            IReadOnlyDictionary<string, object?>? context)
        {
            if (context != null &&
                context.TryGetValue("sourcePinId",     out var pinObj)  && pinObj  is PinId pinId &&
                context.TryGetValue("sourceDirection", out var dirObj)  && dirObj  is PinDirection dir &&
                context.TryGetValue("sourceKind",      out var kindObj) && kindObj is PinKind kind)
            {
                var type = context.TryGetValue("sourceType", out var typeObj) && typeObj is TypeKey t
                    ? t
                    : (TypeKey?)null;

                return _catalog.QueryForPinContext(new PinContextQuery(pinId, dir, kind, type, text));
            }

            return _catalog.Query(new NodeSearchQuery(text));
        }

        public Task<IReadOnlyList<NodeCatalogEntry>> QueryAsync(
            string text,
            IReadOnlyDictionary<string, object?>? context,
            CancellationToken ct)
            => Task.FromResult(Query(text, context));

        public void RenderItem(NodeCatalogEntry item, bool selected, bool keyboardFocused, IPickerRenderContext ctx)
        {
            if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero)
                ImGuiNET.ImGui.TextUnformatted(item.DisplayName);
        }

        public void RenderPreview(NodeCatalogEntry item, IPickerRenderContext ctx)
        {
            if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero)
                ImGuiNET.ImGui.TextUnformatted(item.Kind.Id);
        }

        public bool IsPreviewExpensive(NodeCatalogEntry item) => false;
        public string GetSearchableText(NodeCatalogEntry item) => item.DisplayName;
        public string GetItemKey(NodeCatalogEntry item)        => item.Kind.Id;
        public bool CanAcceptDrop(object payload)              => false;
        public string? GetCategory(NodeCatalogEntry item)      => item.CategoryPath;
        public string? GetIconKey(NodeCatalogEntry item)       => item.IconKey;
        public string? GetDescription(NodeCatalogEntry item)   => item.Description;
    }
}
