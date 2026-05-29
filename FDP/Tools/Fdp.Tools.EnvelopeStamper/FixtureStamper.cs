using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;

namespace Fdp.Tools.EnvelopeStamper;

internal static class FixtureStamper
{
    // Inlined docType string constants (avoids referencing Hrot.Common).
    private const string DocTypeScenario = "Hrot.Scenario";
    private const string DocTypeBlueprint = "Hrot.Blueprints";
    private const string DocTypeRoadNetwork = "Fdp.RoadNetwork";
    private const string DocTypeOrchestratorContext = "Hrot.OrchestratorContext";

    internal record StampResult(int Stamped, int AlreadyStamped, int Skipped, int Errors);

    /// <summary>
    /// Walks all .json files under <paramref name="root"/>, stamps those that
    /// are recognised fixture types and do not yet have a <c>$meta</c> envelope.
    /// </summary>
    internal static StampResult StampDirectory(
        string root, bool dryRun, TextWriter stdout, TextWriter stderr)
    {
        int stamped = 0;
        int alreadyStamped = 0;
        int skipped = 0;
        int errors = 0;

        var files = Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories);

        foreach (var path in files)
        {
            if (ShouldSkipPath(path))
            {
                stdout.WriteLine($"  SKIP (excluded): {path}");
                skipped++;
                continue;
            }

            try
            {
                var text = File.ReadAllText(path);
                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(text);
                }
                catch (JsonException ex)
                {
                    stderr.WriteLine($"  ERROR (parse): {path} — {ex.Message}");
                    errors++;
                    continue;
                }

                if (node is not JsonObject dom)
                {
                    stdout.WriteLine($"  SKIP (not object): {path}");
                    skipped++;
                    continue;
                }

                if (JsonEnvelope.HasEnvelope(dom))
                {
                    stdout.WriteLine($"  SKIP (already stamped): {path}");
                    alreadyStamped++;
                    continue;
                }

                var meta = DetectDocType(dom);
                if (meta is null)
                {
                    stdout.WriteLine($"  SKIP (unknown format): {path}");
                    skipped++;
                    continue;
                }

                if (dryRun)
                {
                    stdout.WriteLine($"  DRY-RUN would stamp: {path} [{meta.DocType} v{meta.SchemaVersion}]");
                }
                else
                {
                    JsonEnvelope.Write(dom, meta);

                    using var stream = File.Open(path, FileMode.Create, FileAccess.Write);
                    using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                    dom.WriteTo(writer);

                    stdout.WriteLine($"  STAMPED: {path} [{meta.DocType} v{meta.SchemaVersion}]");
                }

                stamped++;
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"  ERROR: {path} — {ex.Message}");
                errors++;
            }
        }

        return new StampResult(stamped, alreadyStamped, skipped, errors);
    }

    /// <summary>
    /// Detects the document type from the DOM structure.
    /// Returns <c>null</c> if the document is not a recognised fixture type.
    /// </summary>
    internal static DocumentMeta? DetectDocType(JsonObject dom)
    {
        // Rule (a): lowercase "header" with "subsystemType"
        if (dom.TryGetPropertyValue("header", out JsonNode? headerNodeLower)
            && headerNodeLower is JsonObject headerLower
            && headerLower.TryGetPropertyValue("subsystemType", out JsonNode? subTypeLower)
            && subTypeLower is JsonValue subTypeValueLower)
        {
            var docType = subTypeValueLower.GetValue<string>();
            int schemaVersion = docType == DocTypeOrchestratorContext ? 2 : 1;
            return new DocumentMeta(docType, schemaVersion);
        }

        // Rule (b): uppercase "Header" with "SubsystemType"
        if (dom.TryGetPropertyValue("Header", out JsonNode? headerNodeUpper)
            && headerNodeUpper is JsonObject headerUpper
            && headerUpper.TryGetPropertyValue("SubsystemType", out JsonNode? subTypeUpper)
            && subTypeUpper is JsonValue subTypeValueUpper)
        {
            var docType = subTypeValueUpper.GetValue<string>();
            int schemaVersion = docType == DocTypeOrchestratorContext ? 2 : 1;
            return new DocumentMeta(docType, schemaVersion);
        }

        // Rule (c): "nodes" (JsonArray) + "segments" (JsonArray) at top-level => road network
        if (dom.TryGetPropertyValue("nodes", out JsonNode? nodesNode)
            && nodesNode is JsonArray
            && dom.TryGetPropertyValue("segments", out JsonNode? segmentsNode)
            && segmentsNode is JsonArray)
        {
            return new DocumentMeta(DocTypeRoadNetwork, 1);
        }

        return null;
    }

    /// <summary>
    /// Returns <c>true</c> if the given file path should be excluded from stamping.
    /// </summary>
    internal static bool ShouldSkipPath(string path)
    {
        // Skip files in obj/ or bin/ build output directories.
        if (path.Contains(@"\obj\") || path.Contains("/obj/")
            || path.Contains(@"\bin\") || path.Contains("/bin/"))
            return true;

        // Skip files in third-party or generated directories.
        if (path.Contains(@"\ExtDeps\") || path.Contains("/ExtDeps/"))
            return true;

        if (path.Contains(@"\.tmp\") || path.Contains("/.tmp/"))
            return true;

        if (path.Contains(@"\.claude\") || path.Contains("/.claude/"))
            return true;

        // Skip deliberate bad-meta test fixtures.
        if (path.Contains(@"Fdp.Core.Tests\Serialization\Migrations")
            || path.Contains("Fdp.Core.Tests/Serialization/Migrations"))
            return true;

        // Skip navigation mesh data files.
        if (path.Contains(@"Navigation\data") || path.Contains("Navigation/data"))
            return true;

        // Skip well-known infrastructure/config filenames.
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("xunit.runner.json", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.Equals("launchSettings.json", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.Equals("settings.json", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.Equals("settings.local.json", StringComparison.OrdinalIgnoreCase))
            return true;

        // Skip .deps.json and .runtimeconfig.json files.
        if (fileName.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
