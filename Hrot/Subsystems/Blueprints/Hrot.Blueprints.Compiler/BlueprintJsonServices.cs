using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hrot.Blueprints.Core.Assets;
#if NET8_0_OR_GREATER
using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario;
#endif

namespace Hrot.Blueprints.Core;

public static class BlueprintJsonServices
{
    private static readonly JsonSerializerOptions _options;

    static BlueprintJsonServices()
    {
        var opts = new JsonSerializerOptions
        {
            IncludeFields               = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas         = true,
            ReadCommentHandling         = JsonCommentHandling.Skip,
            WriteIndented               = false,
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        opts.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        _options = opts;
    }

    public static string Serialize(BlueprintAsset asset)
    {
#if NET8_0_OR_GREATER
        // Serialize the asset to a DOM, then stamp $meta as the first property.
        // R6 (2026-08-04): schemaVersion bumped 1 -> 2 -- the five collection-consumer node
        // "kind" tags were renamed Component*->Collection* (see Nodes.cs); v1 assets carrying
        // the old tags are rewritten transparently on read (see Deserialize below).
        var dom = JsonSerializer.SerializeToNode(asset, _options)!.AsObject();
        JsonEnvelope.Write(dom, new DocumentMeta(HrotDocumentTypes.Blueprint, 2));
        return dom.ToJsonString();
#else
        return JsonSerializer.Serialize(asset, _options);
#endif
    }

    /// <summary>
    /// R6 rename (2026-08-04): the five collection-consumer node tags were renamed
    /// Component*->Collection*; v1 assets carrying the old tags are rewritten on read, forever.
    /// </summary>
    private static readonly (string OldTag, string NewTag)[] _legacyNodeKindMap =
    {
        ("ComponentForEach",   "CollectionForEach"),
        ("ComponentItemGet",   "CollectionItemGet"),
        ("ComponentItemCount", "CollectionItemCount"),
        ("ComponentContains",  "CollectionContains"),
        ("ComponentFind",      "CollectionFind"),
    };

    // Note (JM-P2-004): System.Text.Json silently ignores unknown properties (no
    // JsonUnmappedMemberHandling.Disallow in _options), so a Phase 2 envelope with $meta is
    // already handled transparently. Both legacy JSON (no $meta) and Phase 2 JSON ($meta first)
    // are deserialized correctly.
    public static BlueprintAsset? Deserialize(string json)
    {
#if NET8_0_OR_GREATER
        // R6 (2026-08-04): fast-path Ordinal Contains check -- only pay for the DOM walk when an
        // old "kind" tag might be present.
        var needsLegacyRewrite = false;
        foreach (var (oldTag, _) in _legacyNodeKindMap)
        {
            if (json.Contains(oldTag, StringComparison.Ordinal))
            {
                needsLegacyRewrite = true;
                break;
            }
        }

        if (!needsLegacyRewrite)
            return JsonSerializer.Deserialize<BlueprintAsset>(json, _options);

        var node = JsonNode.Parse(json);
        if (node is JsonObject root
            && root.TryGetPropertyValue("Graphs", out var graphsNode)
            && graphsNode is JsonArray graphs)
        {
            foreach (var graphNode in graphs)
            {
                if (graphNode is not JsonObject graphObj
                    || !graphObj.TryGetPropertyValue("Nodes", out var nodesNode)
                    || nodesNode is not JsonArray nodes)
                    continue;

                foreach (var nodeNode in nodes)
                {
                    if (nodeNode is not JsonObject nodeObj
                        || !nodeObj.TryGetPropertyValue("kind", out var kindNode)
                        || kindNode is not JsonValue kindValue
                        || !kindValue.TryGetValue<string>(out var kind))
                        continue;

                    foreach (var (oldTag, newTag) in _legacyNodeKindMap)
                    {
                        if (string.Equals(kind, oldTag, StringComparison.Ordinal))
                        {
                            nodeObj["kind"] = JsonValue.Create(newTag);
                            break;
                        }
                    }
                }
            }
        }

        return node?.Deserialize<BlueprintAsset>(_options);
#else
        // Old assets carrying legacy Component* node kind tags are only ever loaded on net8
        // hosts (editor/compiler tooling); the netstandard2.0 target falls back to a plain
        // deserialize with no legacy-tag rewrite.
        return JsonSerializer.Deserialize<BlueprintAsset>(json, _options);
#endif
    }
}
