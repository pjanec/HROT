using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Scenario;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐⭐ <b>R-1 (AR-8) — the HTTP READ side of the attribute vocabulary</b> (📄 <c>docs/UX/DESIGN_Review_Perspective.md</c> §6.13a). The writes already existed
    /// (<c>POST …/attribute</c>, owner-routed; <c>POST …/component</c>, raw); these are their twins, read through the SAME code the forms and the replica watch read through:
    /// <see cref="AttributeReaders"/> (the vocabulary's read side) and <see cref="ComponentJsonPath"/> (the raw walk). ⭐ The Replay Browser serves the same routes through this
    /// same service over its own sandbox world — no second implementation.
    /// </summary>
    public sealed partial class DebugApiService
    {
        /// <summary>The JSON a vocabulary attribute reads as: a number, an integer, a string, a boolean; a non-finite float as the API's sentinel string.</summary>
        private static JsonNode? AttributeValueNode(AttributeDefinition def, in AttributeValue v)
            => def.Kind switch
            {
                AttributeValueKind.CsString => JsonValue.Create(v.StringValue ?? v.Text.ToString()),
                AttributeValueKind.CsInt32 => JsonValue.Create(v.IntValue),
                AttributeValueKind.CsInt64 => JsonValue.Create(v.LongValue),
                AttributeValueKind.CsFloat32 => FiniteOrSentinel(v.FloatValue),
                AttributeValueKind.Bool => JsonValue.Create(v.BoolValue),
                _ => FiniteOrSentinel(v.DoubleValue),
            };

        private static JsonNode FiniteOrSentinel(double d)
            => double.IsFinite(d) ? JsonValue.Create(d)! : JsonValue.Create(double.IsNaN(d) ? "NaN" : d > 0 ? "Infinity" : "-Infinity")!;

        private static string KindWord(AttributeValueKind k) => k switch
        {
            AttributeValueKind.CsString => "string",
            AttributeValueKind.CsInt32 or AttributeValueKind.CsInt64 => "integer",
            AttributeValueKind.Bool => "boolean",
            _ => "number",
        };

        private static bool IsWildcard(AttributeDefinition d) => d.JsonPath.EndsWith(".*", StringComparison.Ordinal);

        private static string SubPath(AttributeDefinition d, int sub) => d.JsonPath[..^1] + sub.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// <c>GET /entities/{networkId}/attributes[?path=…]</c> — the entity's vocabulary attributes as the world reads them now: <c>{networkId, attributes:[{path, value, kind, unit}], notPresent:[path]}</c>.
        /// No <c>path</c> = every attribute the entity has; a wildcard row expands per sub-index it has (<c>Ammo.0</c>, <c>Ammo.1</c>, <c>Damage.Part.3</c>…). A <c>path</c> is a vocabulary path, or a
        /// wildcard with its sub-index (<c>Ammo.2</c>); one the entity lacks is listed in <c>notPresent</c>. Status 404 = no such entity, 400 = a path the vocabulary does not have. Main thread.
        /// </summary>
        public (JsonNode? result, string? error, int status) GetEntityAttributes(int networkId, IReadOnlyList<string>? paths)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity) || !_world.IsAlive(entity))
                return (null, $"Entity {networkId} not found. List entities with GET /entities.", 404);
            var world = _world;

            var attributes = new JsonArray();
            var notPresent = new JsonArray();
            var subs = new List<int>();

            void Add(AttributeDefinition d, int sub, string path)
            {
                if (AttributeReaders.TryRead(world, entity, d.AttributeId, sub, out var v))
                    attributes.Add(new JsonObject { ["path"] = path, ["value"] = AttributeValueNode(d, v), ["kind"] = KindWord(d.Kind), ["unit"] = d.Unit });
                else notPresent.Add(path);
            }

            if (paths == null || paths.Count == 0)
            {
                foreach (var d in AttributeVocabulary.All)
                {
                    subs.Clear();
                    AttributeReaders.SubIndicesOf(world, entity, d.AttributeId, subs);   // only what the entity HAS: an absent attribute is not an answer here
                    foreach (int sub in subs) Add(d, sub, IsWildcard(d) ? SubPath(d, sub) : d.JsonPath);
                }
            }
            else
            {
                foreach (var raw in paths)
                {
                    var path = raw.Trim();
                    if (path.Length == 0) continue;
                    if (!AttributeReaders.TryResolve(path, out ushort id, out int sub))
                        return (null, $"'{path}' is not an attribute path. List them with GET /attributes/schema.", 400);
                    var d = AttributeReaders.DefinitionOf(id)!;
                    if (IsWildcard(d) && path.EndsWith(".*", StringComparison.Ordinal))
                    {
                        subs.Clear();
                        AttributeReaders.SubIndicesOf(world, entity, id, subs);
                        if (subs.Count == 0) notPresent.Add(path);
                        foreach (int s in subs) Add(d, s, SubPath(d, s));
                    }
                    else Add(d, sub, IsWildcard(d) ? SubPath(d, sub) : d.JsonPath);
                }
            }

            return (new JsonObject { ["networkId"] = networkId, ["attributes"] = attributes, ["notPresent"] = notPresent }, null, 200);
        }

        /// <summary>
        /// <c>GET /entities/{networkId}/field?path=Component.field.0</c> — the RAW read, the twin of <c>POST …/component</c>: the component's JSON (the dump's own serializer) walked by the ONE
        /// <see cref="ComponentJsonPath"/> (case-insensitive keys, a number part indexes an array): <c>{networkId, path, value}</c>. Status 404 = no such entity or nothing at that path, 400 = no path. Main thread.
        /// </summary>
        public (JsonNode? result, string? error, int status) GetEntityField(int networkId, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return (null, "path is required: Component.field (e.g. SimTransform.Position.X, WeaponState.Ammo).", 400);
            if (!_entityMap.TryGetEntity(networkId, out _))
                return (null, $"Entity {networkId} not found. List entities with GET /entities.", 404);
            var dumps = _extraction.ExtractEntities(new List<long> { networkId });
            if (dumps.Count == 0) return (null, $"Entity {networkId} not found. List entities with GET /entities.", 404);

            var dump = DumpToJsonNode(dumps[0]);
            var value = ComponentJsonPath.Get(dump, "Components." + path.Trim());
            if (value is null)
                return (null, $"Nothing at '{path}' on entity {networkId}: the first part is a component name (GET /entities/{networkId} lists them), the rest are its fields (a number indexes an array).", 404);
            return (new JsonObject { ["networkId"] = networkId, ["path"] = path.Trim(), ["value"] = value.DeepClone() }, null, 200);
        }
    }
}
