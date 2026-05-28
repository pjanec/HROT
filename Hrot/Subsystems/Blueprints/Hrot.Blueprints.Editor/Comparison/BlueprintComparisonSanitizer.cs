using System.Text.Json;
using System.Text.Json.Nodes;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;
using AiCatalog = Hrot.Editor.AiShared.Catalog.IAssetCatalog;

namespace Hrot.Blueprints.Editor.Comparison;

/// <summary>
/// Sanitizes Blueprint asset JSON files (<c>.bp.json</c>) for LLM-based comparison.
/// Operates on the JSON DOM. Steps performed:
///   0. Migrate schema via <see cref="IComparisonMigrationAdapter"/>.
///   1. Parse the adapted JSON into a <see cref="JsonNode"/> DOM.
///   2/3. Walk all <c>EditorMetadata</c> objects at root, graph, and node levels:
///        node-level: hoist Comment, strip X/Y and all other keys;
///        graph-level: hoist CanvasComments (text-only), strip Viewport, DockState, NodeViewStates, and all other keys;
///        root-level: strip everything.
///   4. Humanize <c>CallPeerBlueprint</c> nodes by looking up <c>PeerBlueprintId</c> in the catalog.
///   8. Re-serialize with alphabetically sorted keys at every level.
/// The <c>Header</c> object is preserved verbatim (it is structural, not diagnostic).
/// </summary>
public sealed class BlueprintComparisonSanitizer : IAssetComparisonSanitizer
{
    private readonly IComparisonMigrationAdapter _migrationAdapter;
    private readonly IMetaEnvelopeSanitizer _metaSanitizer;
    private readonly AiCatalog _catalog;

    /// <summary>Initializes a new instance with the given adapters and asset catalog.</summary>
    public BlueprintComparisonSanitizer(
        IComparisonMigrationAdapter migrationAdapter,
        IMetaEnvelopeSanitizer metaSanitizer,
        AiCatalog catalog)
    {
        _migrationAdapter = migrationAdapter;
        _metaSanitizer    = metaSanitizer;
        _catalog          = catalog;
    }

    /// <inheritdoc/>
    public AssetKind TargetKind => AssetKind.Blueprint;

    /// <inheritdoc/>
    public SanitizationResult Sanitize(AssetExportRequest request)
    {
        try
        {
            return SanitizeCore(request);
        }
        catch (Exception ex)
        {
            string rawText = TryReadFile(request.AssetMainFilePath);
            return new SanitizationResult(
                rawText,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning($"Sanitization failed unexpectedly: {ex.Message}") });
        }
    }

    // ---- Core pipeline ----

    private SanitizationResult SanitizeCore(AssetExportRequest request)
    {
        var warnings = new List<SanitizationWarning>();

        if (!File.Exists(request.AssetMainFilePath))
        {
            return new SanitizationResult(
                string.Empty,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning($"File not found: {request.AssetMainFilePath}") });
        }

        string rawJson = File.ReadAllText(request.AssetMainFilePath);

        // Step 0: migrate schema.
        string adaptedJson = _migrationAdapter.Adapt(rawJson, out bool didMigrate);
        string? migrationNotice = didMigrate ? "Document was migrated to the current schema version." : null;

        // Step 1: parse DOM.
        JsonNode? root = JsonNode.Parse(adaptedJson);
        if (root is not JsonObject rootObj)
        {
            return new SanitizationResult(
                rawJson,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning("Blueprint JSON root is not an object.") });
        }

        // Steps 2/3: strip EditorMetadata at root, graph, and node levels.
        ProcessRootEditorMetadata(rootObj);
        ProcessGraphs(rootObj);

        // Step 4: humanize CallPeerBlueprint nodes.
        HumanizePeerCalls(rootObj);

        // Step 8: sort and re-serialize.
        JsonNode sorted = SortPropertiesRecursive(rootObj);
        var serializerOptions = new JsonSerializerOptions { WriteIndented = true };
        string sanitizedText = sorted.ToJsonString(serializerOptions);

        // Extract metadata.
        AssetMetadataBlock metadata = BuildMetadata(request, rootObj, migrationNotice);

        return new SanitizationResult(sanitizedText, metadata, warnings);
    }

    // ---- EditorMetadata processing ----

    /// <summary>Strips the root-level EditorMetadata entirely (no semantic content).</summary>
    private static void ProcessRootEditorMetadata(JsonObject root)
    {
        root.Remove("EditorMetadata");
    }

    /// <summary>Processes every graph's EditorMetadata and all node EditorMetadata within each graph.</summary>
    private static void ProcessGraphs(JsonObject root)
    {
        if (root["Graphs"] is not JsonArray graphs)
            return;

        foreach (JsonNode? graphNode in graphs)
        {
            if (graphNode is not JsonObject graph)
                continue;

            ProcessGraphEditorMetadata(graph);
            ProcessNodeList(graph);
        }
    }

    /// <summary>
    /// Processes graph-level EditorMetadata:
    /// hoists CanvasComments (text-only) to <c>_canvasComments</c>,
    /// strips Viewport, DockState, NodeViewStates, and any unrecognized keys.
    /// </summary>
    private static void ProcessGraphEditorMetadata(JsonObject graph)
    {
        if (graph["EditorMetadata"] is not JsonObject meta)
        {
            graph.Remove("EditorMetadata");
            return;
        }

        // Hoist CanvasComments: keep only "Text" from each entry.
        if (meta["CanvasComments"] is JsonArray canvasComments)
        {
            var hoisted = new JsonArray();
            foreach (JsonNode? commentNode in canvasComments)
            {
                var entry = new JsonObject();
                if (commentNode is JsonObject comment && comment["Text"]?.GetValue<string>() is string text)
                    entry["Text"] = text;
                hoisted.Add(entry);
            }
            graph["_canvasComments"] = hoisted;
        }

        // Remove graph EditorMetadata entirely (all remaining keys are stripped).
        graph.Remove("EditorMetadata");
    }

    /// <summary>Processes every node in a graph's Nodes array.</summary>
    private static void ProcessNodeList(JsonObject graph)
    {
        if (graph["Nodes"] is not JsonArray nodes)
            return;

        foreach (JsonNode? nodeElement in nodes)
        {
            if (nodeElement is JsonObject node)
                ProcessNodeEditorMetadata(node);
        }
    }

    /// <summary>
    /// Processes node-level EditorMetadata:
    /// hoists Comment to top-level node property, strips X, Y, and all other keys.
    /// Removes EditorMetadata from the node if empty after processing.
    /// </summary>
    private static void ProcessNodeEditorMetadata(JsonObject node)
    {
        if (node["EditorMetadata"] is not JsonObject meta)
        {
            node.Remove("EditorMetadata");
            return;
        }

        // Hoist Comment.
        if (meta["Comment"] is JsonNode commentValue)
        {
            string? comment = commentValue.GetValue<string>();
            if (comment != null)
                node["Comment"] = comment;
        }

        // Remove EditorMetadata entirely (X, Y, Comment, and all other keys stripped).
        node.Remove("EditorMetadata");
    }

    // ---- CallPeerBlueprint humanization ----

    private void HumanizePeerCalls(JsonObject root)
    {
        if (root["Graphs"] is not JsonArray graphs)
            return;

        foreach (JsonNode? graphNode in graphs)
        {
            if (graphNode is not JsonObject graph)
                continue;

            if (graph["Nodes"] is not JsonArray nodes)
                continue;

            foreach (JsonNode? nodeElement in nodes)
            {
                if (nodeElement is not JsonObject node)
                    continue;

                string? kind = node["kind"]?.GetValue<string>();
                if (kind != "CallPeerBlueprint")
                    continue;

                string? peerId = node["PeerBlueprintId"]?.GetValue<string>();
                if (peerId == null || !Guid.TryParse(peerId, out Guid peerGuid))
                    continue;

                IEditableAsset? asset = _catalog.FindByAssetId(peerGuid);
                node["_targetName"] = asset != null
                    ? $"{asset.Name} ({asset.Kind})"
                    : "(asset not found in catalog)";
            }
        }
    }

    // ---- Alphabetical sort ----

    private static JsonNode SortPropertiesRecursive(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var sorted = new JsonObject();
                foreach (var kv in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    sorted[kv.Key] = kv.Value != null ? SortPropertiesRecursive(kv.Value.DeepClone()) : null;
                return sorted;
            }
            case JsonArray arr:
            {
                var result = new JsonArray();
                foreach (var item in arr)
                    result.Add(item != null ? SortPropertiesRecursive(item.DeepClone()) : null);
                return result;
            }
            default:
                return node.DeepClone();
        }
    }

    // ---- Metadata extraction ----

    private static AssetMetadataBlock BuildMetadata(
        AssetExportRequest request,
        JsonObject root,
        string? migrationNotice)
    {
        string assetName = root["Name"]?.GetValue<string>() ?? "(unknown)";
        Guid assetId = Guid.Empty;
        if (root["AssetId"]?.GetValue<string>() is string idStr)
            Guid.TryParse(idStr, out assetId);
        DateTime? timestamp = TryGetLastWriteTime(request.AssetMainFilePath);

        return new AssetMetadataBlock(
            assetName,
            AssetKind.Blueprint,
            assetId,
            request.AssetMainFilePath,
            Array.Empty<string>(),
            timestamp,
            migrationNotice);
    }

    private static AssetMetadataBlock BuildFallbackMetadata(AssetExportRequest request)
    {
        return new AssetMetadataBlock(
            "(unknown)",
            AssetKind.Blueprint,
            Guid.Empty,
            request.AssetMainFilePath,
            Array.Empty<string>(),
            TryGetLastWriteTime(request.AssetMainFilePath));
    }

    // ---- File helpers ----

    private static string TryReadFile(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : string.Empty; }
        catch { return string.Empty; }
    }

    private static DateTime? TryGetLastWriteTime(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null; }
        catch { return null; }
    }
}
