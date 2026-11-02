using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐ Node Features F-16 / R-134 — a cluster feature override as every layer above the wire sees it: the feature, the node NAMES it applies to
    /// (empty = all), and a PARTIAL JSON object. The DDS twin (<c>FeatureOverrideWire</c>) and the JSON DTO are converted at the edge only.
    /// 📄 docs/DESIGN_Node_Features.md §7.1.
    /// </summary>
    public sealed record FeatureOverrideSpec(string Feature, IReadOnlyList<string> Nodes, string Json);

    /// <summary>Flat JSON-object merging for feature layers.</summary>
    public static class FeatureJson
    {
        /// <summary>Overlays the top-level keys of <paramref name="over"/> on <paramref name="under"/> (flat, like <c>OverlayFlat</c>); either may be empty.</summary>
        public static string MergeFlat(string? under, string? over)
        {
            if (string.IsNullOrWhiteSpace(over)) return string.IsNullOrWhiteSpace(under) ? "{}" : under!;
            if (string.IsNullOrWhiteSpace(under)) return over!;
            var a = (JsonNode.Parse(under!) as JsonObject) ?? new JsonObject();
            var b = (JsonNode.Parse(over!) as JsonObject) ?? new JsonObject();
            foreach (var kv in b) a[kv.Key] = kv.Value?.DeepClone();
            return a.ToJsonString();
        }
    }

    /// <summary>
    /// ⭐ Node Features F-14 / F-15 — what the recording and load machinery asks of the node's feature reader, without depending on Hrot.
    /// Implemented by <c>NodeFeatures</c>; reached as <c>kernel.FeatureGate as IFeatureRecording</c>.
    /// </summary>
    public interface IFeatureRecording
    {
        /// <summary>The effective FULL config (JSON) of every AtLoad feature this node has — what the recording keeps.</summary>
        IReadOnlyDictionary<string, string> AtLoadConfigs();

        /// <summary>A load starts: freeze the AtLoad features to the cluster layer the master stamped for this node (id → partial JSON).</summary>
        void FreezeAtLoad(IReadOnlyDictionary<string, string>? clusterLayer);

        /// <summary>A branch to live from a recording: the recorded FULL configs win. THROWS when the build does not declare a recorded feature or its type lost a key — never a silent different run.</summary>
        void FreezeRecorded(IReadOnlyDictionary<string, string> recorded);
    }
}
