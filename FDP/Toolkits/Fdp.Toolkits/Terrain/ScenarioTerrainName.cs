using System.IO;
using System.Text.Json;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// Reads the terrain a node's STAGED scenario names.
    ///
    /// <para>⭐ One implementation, because two callers need it for opposite reasons and they must not
    /// disagree: the terrain LOADER asks "what should I load?", and the identity CHECK (§8.3 N4) asks
    /// "did I end up holding it?". If those two read the header differently, a node could load one
    /// terrain and pass a check against another.</para>
    ///
    /// <para>⚠ The header lives beside the staged TKB artefact — <c>{stagingRoot}/TKB/ScenarioHeader.json</c>
    /// — and is put there by the prefetch handler that EVERY host registers, which is what makes the
    /// check uniform rather than a SimHost privilege.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1e ①, §8.3 N4.
    /// </summary>
    public static class ScenarioTerrainName
    {
        /// <summary>
        /// The <c>TerrainName</c> from the staged scenario header, or <see langword="null"/> when the
        /// header is absent or names no terrain. ⭐ Null is LEGAL — a scenario with no terrain loads
        /// anyway, exactly like one with no TKB.
        /// </summary>
        public static string? Read(string? localStagingRoot)
        {
            if (string.IsNullOrWhiteSpace(localStagingRoot)) return null;

            string headerPath = Path.Combine(localStagingRoot, "TKB", "ScenarioHeader.json");
            if (!File.Exists(headerPath)) return null;

            // Forward-only — no DOM allocation for a one-property peek.
            var reader = new Utf8JsonReader(File.ReadAllBytes(headerPath));
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName &&
                    reader.ValueTextEquals("TerrainName"))
                {
                    reader.Read();
                    return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }
            }
            return null;
        }
    }
}
