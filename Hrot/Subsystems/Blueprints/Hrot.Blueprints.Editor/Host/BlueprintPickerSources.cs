using Hrot.Blueprints.Core.Assets;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Registers blueprint-backed <see cref="IPickerSource{TItem}"/> implementations
/// into the picker registry (BCP-E).
///
/// <para>
/// Registered sources:
/// <list type="bullet">
///   <item><c>nodes.all</c> — all node kinds from <see cref="BlueprintNodeCatalog"/>.</item>
///   <item><c>nodes.by-pin</c> — node kinds filtered by dragged pin compatibility.</item>
///   <item><c>variables.all</c> — all variables declared in the active <see cref="BlueprintAsset"/>.</item>
///   <item><c>types.all</c> — representative type set from <see cref="BlueprintTypeSystem"/>.</item>
///   <item><c>assets.by-type</c> — placeholder asset-grid source.</item>
///   <item><c>enum.values</c> — placeholder flags/enum source.</item>
/// </list>
/// </para>
/// </summary>
public static class BlueprintPickerSources
{
    /// <summary>
    /// Register all blueprint picker sources into <paramref name="registry"/>.
    /// Safe to call multiple times on the same registry instance — later registrations
    /// overwrite earlier ones.
    /// </summary>
    /// <param name="currentGraph">
    /// BP-57 — the canvas's current graph, so <c>variables.all</c> can offer that graph's LOCALS
    /// alongside the asset's variables. ⚠ A delegate, never a captured <c>Graph</c>: the picker must
    /// follow a BP-24 graph switch (BP-72's lesson). Optional so existing call sites keep compiling;
    /// when null the picker offers asset variables only, exactly as before.
    /// </param>
    public static void Register(
        IPickerRegistry    registry,
        BlueprintNodeCatalog catalog,
        BlueprintAsset     asset,
        Func<Graph?>?      currentGraph = null)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (catalog  is null) throw new ArgumentNullException(nameof(catalog));
        if (asset    is null) throw new ArgumentNullException(nameof(asset));

        var nodePicker = new BlueprintNodePickerSource(catalog);
        registry.Register("nodes.all",   nodePicker);
        registry.Register("nodes.by-pin", nodePicker);

        registry.Register("variables.all", new BlueprintVariablePickerSource(asset, currentGraph));
        registry.Register("types.all",     new BlueprintTypePickerSource());
        registry.Register("assets.by-type", new BlueprintAssetGridPickerSource());
        registry.Register("enum.values",    new BlueprintEnumPickerSource());
    }

    // ── nodes.all / nodes.by-pin ─────────────────────────────────────────────

    /// <summary>
    /// Node picker backed by <see cref="BlueprintNodeCatalog"/>.
    /// When the context carries <c>sourcePinId</c>, <c>sourceDirection</c>,
    /// <c>sourceKind</c> and optionally <c>sourceType</c>, the results are
    /// filtered via <see cref="BlueprintNodeCatalog.QueryForPinContext"/> — mirroring
    /// <c>FakeNodePickerSource</c> from the NodeEdit demo.
    /// </summary>
    internal sealed class BlueprintNodePickerSource : IPickerSource<NodeCatalogEntry>
    {
        private readonly BlueprintNodeCatalog _catalog;

        public BlueprintNodePickerSource(BlueprintNodeCatalog catalog)
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

    // ── variables.all ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the variables declared in the active <see cref="BlueprintAsset"/>,
    /// filtered by the search text.
    /// </summary>
    internal sealed class BlueprintVariablePickerSource : IPickerSource<VariableDecl>
    {
        private readonly BlueprintAsset _asset;
        private readonly Func<Graph?>?  _currentGraph;

        public BlueprintVariablePickerSource(BlueprintAsset asset, Func<Graph?>? currentGraph = null)
        {
            _asset        = asset;
            _currentGraph = currentGraph;
        }

        /// <summary>
        /// BP-57 — the current graph's locals, or empty. ⚠ Resolved per call, never captured, so the
        /// picker follows a graph switch.
        /// </summary>
        private IReadOnlyList<VariableDecl> Locals
            => (IReadOnlyList<VariableDecl>?)_currentGraph?.Invoke()?.LocalVariables
               ?? Array.Empty<VariableDecl>();

        /// <summary>
        /// ⭐ Identity, not name. <c>Q27-C1</c> lets a local SHADOW an asset variable of the same name,
        /// so a name test would mislabel the shadowed pair — which is the very confusion the
        /// <c>(local)</c> suffix exists to remove.
        /// </summary>
        private bool IsLocal(VariableDecl v)
        {
            foreach (var l in Locals) if (ReferenceEquals(l, v)) return true;
            return false;
        }

        public string Title             => "Pick Variable";
        public string EmptyResultText   => "No variables declared.";
        public PickerLayout PreferredLayout => PickerLayout.Compact;
        public PickerSelectionMode SelectionMode => PickerSelectionMode.Single;
        public QueryCost Cost           => QueryCost.Cheap;
        public bool IsAsync             => false;
        public bool AllowsDragOut       => false;
        public bool AllowsDragIn        => false;
        public bool AllowArbitraryTextInput => false;

        /// <summary>
        /// BP-57 — the asset's variables, then the current graph's LOCALS.
        ///
        /// <para>
        /// ⭐ <b>Until this, a local could not be aimed at from the editor at all</b> — one could be
        /// declared in JSON and the compiler would honour it, but no picker would ever offer it.
        /// </para>
        ///
        /// <para>
        /// ⛔⛔ <b>Deliberately NOT widened further.</b> <c>WorkingState</c>/<c>Parameters</c> are
        /// <c>BP-226</c>'s positional index space and it is unfixed — offering them is precisely what
        /// makes that row live. Struct FQNs are <c>BP-228</c>'s: the compiler validates <b>nothing</b>
        /// there, so <c>a.b</c> compiles and emits <c>global::a.b</c>. Both are the unification's, not
        /// this batch's.
        /// </para>
        /// </summary>
        public IReadOnlyList<VariableDecl> Query(
            string text,
            IReadOnlyDictionary<string, object?>? context)
        {
            var locals = Locals;
            if (string.IsNullOrEmpty(text))
                return locals.Count == 0
                    ? _asset.Variables
                    : _asset.Variables.Concat(locals).ToList();

            return _asset.Variables.Concat(locals)
                .Where(v => v.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public Task<IReadOnlyList<VariableDecl>> QueryAsync(
            string text,
            IReadOnlyDictionary<string, object?>? context,
            CancellationToken ct)
            => Task.FromResult(Query(text, context));

        /// <summary>
        /// ⚠ <b>The <c>(local)</c> suffix is load-bearing, not decoration.</b> <c>Q27-C1</c> lets a
        /// local shadow an asset variable of the same name and the compiler resolves it silently and
        /// correctly — so without this the picker shows <b>two identical rows that read different
        /// storage</b>, and the designer has no way to tell which one they are choosing.
        /// </summary>
        public void RenderItem(VariableDecl item, bool selected, bool keyboardFocused, IPickerRenderContext ctx)
        {
            if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero)
                ImGuiNET.ImGui.TextUnformatted(RowLabel(item));
        }

        /// <summary>The rendered row text, extracted so a headless test can assert it without ImGui.</summary>
        internal string RowLabel(VariableDecl item)
            => IsLocal(item)
                ? $"{item.Name} : {item.Type?.TypeId ?? "?"}   (local)"
                : $"{item.Name} : {item.Type?.TypeId ?? "?"}";

        public void RenderPreview(VariableDecl item, IPickerRenderContext ctx)
        {
            if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero)
                ImGuiNET.ImGui.TextDisabled(item.Type?.TypeId ?? "");
        }

        public bool IsPreviewExpensive(VariableDecl item) => false;
        public string GetSearchableText(VariableDecl item) => item.Name;
        public string GetItemKey(VariableDecl item)        => item.Id.ToString();
        public bool CanAcceptDrop(object payload)          => false;
    }

    // ── types.all ────────────────────────────────────────────────────────────

    /// <summary>
    /// Provides the representative type vocabulary from <see cref="BlueprintTypeSystem"/>.
    /// </summary>
    internal sealed class BlueprintTypePickerSource : IPickerSource<TypeKey>
    {
        // Representative types exposed by the Blueprint type system.
        private static readonly IReadOnlyList<TypeKey> _types = new[]
        {
            new TypeKey("System.Boolean"),
            new TypeKey("System.Int32"),
            new TypeKey("System.Single"),
            new TypeKey("System.Double"),
            new TypeKey("System.String"),
            new TypeKey("System.Object"),
            new TypeKey("System.Numerics.Vector2"),
            new TypeKey("System.Numerics.Vector3"),
            new TypeKey("System.Numerics.Quaternion"),
        };

        public string Title             => "Pick Type";
        public string EmptyResultText   => "No types match.";
        public PickerLayout PreferredLayout => PickerLayout.Standard;
        public PickerSelectionMode SelectionMode => PickerSelectionMode.Single;
        public QueryCost Cost           => QueryCost.Cheap;
        public bool IsAsync             => false;
        public bool AllowsDragOut       => false;
        public bool AllowsDragIn        => false;
        public bool AllowArbitraryTextInput => false;

        public IReadOnlyList<TypeKey> Query(
            string text,
            IReadOnlyDictionary<string, object?>? context)
        {
            if (string.IsNullOrEmpty(text))
                return _types;

            return _types
                .Where(t => t.Id.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public Task<IReadOnlyList<TypeKey>> QueryAsync(
            string text,
            IReadOnlyDictionary<string, object?>? context,
            CancellationToken ct)
            => Task.FromResult(Query(text, context));

        public void RenderItem(TypeKey item, bool selected, bool keyboardFocused, IPickerRenderContext ctx)
        {
            if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero)
                ImGuiNET.ImGui.TextUnformatted(item.Id.Split('.').Last());
        }

        public void RenderPreview(TypeKey item, IPickerRenderContext ctx)
        {
            if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero)
                ImGuiNET.ImGui.TextDisabled(item.Id);
        }

        public bool IsPreviewExpensive(TypeKey item) => false;
        public string GetSearchableText(TypeKey item) => item.Id;
        public string GetItemKey(TypeKey item)        => item.Id;
        public bool CanAcceptDrop(object payload)     => false;
    }

    // ── assets.by-type ───────────────────────────────────────────────────────

    /// <summary>
    /// Placeholder asset-grid picker source (BCP-E; full asset catalog integration is out of scope here).
    /// </summary>
    internal sealed class BlueprintAssetGridPickerSource : IPickerSource<string>
    {
        public string Title             => "Pick Asset";
        public string EmptyResultText   => "No assets found.";
        public PickerLayout PreferredLayout => PickerLayout.Grid;
        public PickerSelectionMode SelectionMode => PickerSelectionMode.Single;
        public QueryCost Cost           => QueryCost.Moderate;
        public bool IsAsync             => false;
        public bool AllowsDragOut       => false;
        public bool AllowsDragIn        => false;
        public bool AllowArbitraryTextInput => false;

        public IReadOnlyList<string> Query(string text, IReadOnlyDictionary<string, object?>? context)
            => Array.Empty<string>();

        public Task<IReadOnlyList<string>> QueryAsync(string text, IReadOnlyDictionary<string, object?>? context, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public void RenderItem(string item, bool selected, bool keyboardFocused, IPickerRenderContext ctx)
        {
            if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero)
                ImGuiNET.ImGui.TextUnformatted(item);
        }

        public void RenderPreview(string item, IPickerRenderContext ctx) { }
        public bool IsPreviewExpensive(string item) => false;
        public string GetSearchableText(string item) => item;
        public string GetItemKey(string item)        => item;
        public bool CanAcceptDrop(object payload)    => false;
    }

    // ── enum.values ──────────────────────────────────────────────────────────

    /// <summary>
    /// Placeholder flags/enum picker source (BCP-E; full enum reflection is out of scope here).
    /// </summary>
    internal sealed class BlueprintEnumPickerSource : IPickerSource<string>
    {
        public string Title             => "Pick Enum Value";
        public string EmptyResultText   => "No enum values.";
        public PickerLayout PreferredLayout => PickerLayout.Compact;
        public PickerSelectionMode SelectionMode => PickerSelectionMode.Multi;
        public QueryCost Cost           => QueryCost.Cheap;
        public bool IsAsync             => false;
        public bool AllowsDragOut       => false;
        public bool AllowsDragIn        => false;
        public bool AllowArbitraryTextInput => false;

        public IReadOnlyList<string> Query(string text, IReadOnlyDictionary<string, object?>? context)
            => Array.Empty<string>();

        public Task<IReadOnlyList<string>> QueryAsync(string text, IReadOnlyDictionary<string, object?>? context, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public void RenderItem(string item, bool selected, bool keyboardFocused, IPickerRenderContext ctx)
        {
            if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero)
                ImGuiNET.ImGui.TextUnformatted(item);
        }

        public void RenderPreview(string item, IPickerRenderContext ctx) { }
        public bool IsPreviewExpensive(string item) => false;
        public string GetSearchableText(string item) => item;
        public string GetItemKey(string item)        => item;
        public bool CanAcceptDrop(object payload)    => false;
    }
}
