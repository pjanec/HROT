using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Demo.FakeBlueprint;

public sealed class FakeNodePickerSource : IPickerSource<NodeCatalogEntry>
{
    private readonly INodeCatalog _catalog;

    public FakeNodePickerSource(INodeCatalog catalog) => _catalog = catalog;

    public string Title => "Add Node";
    public string EmptyResultText => "No matching nodes.";
    public PickerLayout PreferredLayout => PickerLayout.Wide;
    public PickerSelectionMode SelectionMode => PickerSelectionMode.Single;
    public QueryCost Cost => QueryCost.Cheap;
    public bool IsAsync => false;
    public bool AllowsDragOut => false;
    public bool AllowsDragIn => false;
    public bool AllowArbitraryTextInput => false;

    public IReadOnlyList<NodeCatalogEntry> Query(string text, IReadOnlyDictionary<string, object?>? context)
    {
        if (context != null &&
            context.TryGetValue("sourcePinId", out var pinObj) &&
            pinObj is PinId pinId &&
            context.TryGetValue("sourceDirection", out var dirObj) &&
            dirObj is PinDirection dir &&
            context.TryGetValue("sourceKind", out var kindObj) &&
            kindObj is PinKind kind)
        {
            var type = context.TryGetValue("sourceType", out var typeObj) && typeObj is TypeKey t
                ? t
                : (TypeKey?)null;

            return _catalog.QueryForPinContext(
                new PinContextQuery(pinId, dir, kind, type, text));
        }

        return _catalog.Query(new NodeSearchQuery(text));
    }

    public Task<IReadOnlyList<NodeCatalogEntry>> QueryAsync(
        string text,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken ct)
        => Task.FromResult(Query(text, context));

    public void RenderItem(NodeCatalogEntry item, bool selected, bool keyboardFocused, IPickerRenderContext ctx)
        => ImGui.TextUnformatted(item.DisplayName);

    public void RenderPreview(NodeCatalogEntry item, IPickerRenderContext ctx)
        => ImGui.TextUnformatted(item.Kind.Id);

    public bool IsPreviewExpensive(NodeCatalogEntry item) => false;

    public string GetSearchableText(NodeCatalogEntry item) => item.DisplayName;

    public string GetItemKey(NodeCatalogEntry item) => item.Kind.Id;

    public bool CanAcceptDrop(object payload) => false;
}
