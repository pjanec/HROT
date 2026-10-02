using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// Reads a terrain definition asset (JSON) into a <see cref="TerrainDefinition"/>.
    ///
    /// <para><b>⭐ Fails loudly, never silently.</b> A terrain that cannot be parsed is a broken
    /// configuration, not an absent one: loading the terrain a scenario names is mandatory, so a
    /// malformed or future-versioned asset throws rather than yielding an empty definition that would
    /// look exactly like "this terrain provides nothing".</para>
    ///
    /// <para>⚠ The one thing that is NOT an error is a definition that declares no content — a terrain
    /// with no road networks yet is legal and parses to an empty, valid definition.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1e ①a.
    /// </summary>
    public static class TerrainDefinitionParser
    {
        /// <summary>Parses a definition from JSON text.</summary>
        /// <exception cref="ArgumentException">The text is empty, is not a JSON object, or its schema
        /// version is missing or newer than this build understands.</exception>
        public static TerrainDefinition Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Terrain definition is empty.", nameof(json));

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Terrain definition is not valid JSON: {ex.Message}", nameof(json), ex);
            }

            if (root is not JsonObject obj)
                throw new ArgumentException("Terrain definition root must be a JSON object.", nameof(json));

            // ── The versioned root ────────────────────────────────────────────────────────────────
            if (obj["schemaVersion"] is not JsonNode versionNode)
                throw new ArgumentException(
                    "Terrain definition has no 'schemaVersion'. A versioned root is required so the "
                  + "format can grow without silently misreading older assets.", nameof(json));

            int version = versionNode.GetValue<int>();
            if (version <= 0 || version > TerrainDefinition.CurrentSchemaVersion)
                throw new ArgumentException(
                    $"Terrain definition schemaVersion {version} is not supported by this build "
                  + $"(understands 1..{TerrainDefinition.CurrentSchemaVersion}).", nameof(json));

            var roads = new List<string>();
            if (obj["roadNetworks"] is JsonArray roadArray)
            {
                foreach (var node in roadArray)
                {
                    var path = node?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(path))
                        roads.Add(path!);
                }
            }

            return new TerrainDefinition
            {
                SchemaVersion = version,
                Name          = obj["name"]?.GetValue<string>() ?? string.Empty,
                RoadNetworks  = roads,
            };
        }
    }
}
