using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario;
using Hrot.Common.Scenario.Migrations;

namespace Hrot.Common.Tests.Scenario.Migrations;

/// <summary>
/// JM-P2-011: Phase 2 gate convention tests.
/// Verifies all committed fixture files have a valid $meta envelope, and that
/// loading via ReadOnlyMigrationAdapter produces a well-formed, round-trippable DOM.
/// </summary>
public sealed class Phase2ConventionTests
{
    // ── T_Conv_01 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// All committed fixture JSON files have a valid $meta envelope.
    /// Verifies Phase 2 success condition: every known fixture was stamped.
    /// </summary>
    [Fact]
    public void AllCommittedFixtures_HaveValidMetaEnvelope()
    {
        string root = FindWorkspaceRoot();
        var knownFixtures = new List<string>();

        foreach (string path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            if (ShouldSkipPath(path))
                continue;

            JsonObject? dom = TryParseJsonObject(path);
            if (dom is null)
                continue;

            if (!IsKnownFixture(dom))
                continue;

            knownFixtures.Add(path);
        }

        Assert.True(knownFixtures.Count >= 10,
            $"Expected at least 10 known fixtures but found {knownFixtures.Count}. " +
            $"Workspace root: {root}. Workspace root discovery or fixture walk may have failed.");

        var failures = new List<string>();
        foreach (string path in knownFixtures)
        {
            try
            {
                JsonEnvelope.Peek(path);
            }
            catch (Exception ex)
            {
                failures.Add($"{path}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} fixture(s) missing a valid $meta envelope:\n" +
            string.Join("\n", failures));
    }

    // ── T_Conv_02 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// All committed scenario fixtures carry docType="Hrot.Scenario" and schemaVersion=1.
    /// </summary>
    [Fact]
    public void AllScenarioFixtures_HaveCorrectDocTypeAndVersion()
    {
        string root = FindWorkspaceRoot();
        var scenarioFiles = new List<string>();

        foreach (string path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            if (ShouldSkipPath(path))
                continue;

            JsonObject? dom = TryParseJsonObject(path);
            if (dom is null)
                continue;

            if (IsScenarioFixture(dom))
                scenarioFiles.Add(path);
        }

        Assert.NotEmpty(scenarioFiles);

        var failures = new List<string>();
        foreach (string path in scenarioFiles)
        {
            try
            {
                DocumentMeta meta = JsonEnvelope.Peek(path);
                if (!string.Equals(meta.DocType, HrotDocumentTypes.Scenario, StringComparison.Ordinal)
                    || meta.SchemaVersion < 1 || meta.SchemaVersion > ScenarioMigrationModule.CurrentVersion)
                {
                    failures.Add(
                        $"{path}: DocType={meta.DocType}, SchemaVersion={meta.SchemaVersion}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{path}: threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} scenario fixture(s) have incorrect meta values:\n" +
            string.Join("\n", failures));
    }

    // ── T_Conv_03 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// All committed blueprint fixtures carry docType="Hrot.Blueprints" and schemaVersion=1.
    /// </summary>
    [Fact]
    public void AllBlueprintFixtures_HaveCorrectDocTypeAndVersion()
    {
        string root = FindWorkspaceRoot();
        var blueprintFiles = new List<string>();

        foreach (string path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            if (ShouldSkipPath(path))
                continue;

            JsonObject? dom = TryParseJsonObject(path);
            if (dom is null)
                continue;

            if (IsBlueprintFixture(dom))
                blueprintFiles.Add(path);
        }

        Assert.NotEmpty(blueprintFiles);

        var failures = new List<string>();
        foreach (string path in blueprintFiles)
        {
            try
            {
                DocumentMeta meta = JsonEnvelope.Peek(path);
                if (!string.Equals(meta.DocType, HrotDocumentTypes.Blueprint, StringComparison.Ordinal)
                    || meta.SchemaVersion != 1)
                {
                    failures.Add(
                        $"{path}: DocType={meta.DocType}, SchemaVersion={meta.SchemaVersion}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{path}: threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} blueprint fixture(s) have incorrect meta values:\n" +
            string.Join("\n", failures));
    }

    // ── T_Conv_04 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Round-trip: load a scenario via ReadOnlyMigrationAdapter, verify DOM has $meta
    /// as first property with correct values, then re-serialize and re-parse and verify
    /// the same invariants hold.
    /// </summary>
    [Fact]
    public async Task LoadScenario_ViaReadOnlyAdapter_DomHasValidMetaAndRoundTrips()
    {
        string root = FindWorkspaceRoot();
        string scenarioPath = Path.Combine(root, "scenarios", "hill-attack", "scenario.json");

        Assert.True(File.Exists(scenarioPath), $"Scenario fixture not found: {scenarioPath}");

        MigrationServices services = HrotMigrationBootstrap.BuildSimHostCgf("Hrot.SimHost");
        var outcome = await services.ReadOnly.LoadAndMigrateAsync(scenarioPath);

        JsonObject dom = outcome.AsJsonObject();

        // $meta must be the first property in the DOM.
        Assert.True(dom.Count > 0, "Loaded DOM is empty.");
        Assert.Equal("$meta", dom.First().Key);

        // $meta must carry the expected docType and schemaVersion.
        // Phase 3: CurrentVersion is now 2, so v1 files are migrated to v2.
        DocumentMeta meta = JsonEnvelope.Read(dom);
        Assert.Equal(HrotDocumentTypes.Scenario, meta.DocType);
        Assert.Equal(ScenarioMigrationModule.CurrentVersion, meta.SchemaVersion);

        // Round-trip: serialize DOM back to JSON string and re-parse.
        string serialized = dom.ToJsonString();
        JsonObject dom2 = JsonNode.Parse(serialized)!.AsObject();

        // $meta must still be the first property after round-trip.
        Assert.Equal("$meta", dom2.First().Key);

        // $meta values must be preserved.
        DocumentMeta meta2 = JsonEnvelope.Read(dom2);
        Assert.Equal(HrotDocumentTypes.Scenario, meta2.DocType);
        Assert.Equal(ScenarioMigrationModule.CurrentVersion, meta2.SchemaVersion);

        // Legacy "header" field must still be present (backward-compat field preserved).
        Assert.True(dom2.ContainsKey("header"),
            "Round-tripped DOM does not contain legacy 'header' field.");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string FindWorkspaceRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IOS-IG-SimHost.sln")))
            dir = dir.Parent;
        if (dir == null)
            throw new InvalidOperationException(
                "Cannot locate workspace root (IOS-IG-SimHost.sln not found). " +
                $"Started search from: {AppContext.BaseDirectory}");
        return dir.FullName;
    }

    private static JsonObject? TryParseJsonObject(string path)
    {
        try
        {
            string text = File.ReadAllText(path);
            JsonNode? node = JsonNode.Parse(text);
            return node as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns true if the DOM is a known fixture type (scenario, blueprint, road network,
    /// or any document with a header.subsystemType / Header.SubsystemType field).
    /// </summary>
    private static bool IsKnownFixture(JsonObject dom)
    {
        // Lowercase header.subsystemType
        if (dom.TryGetPropertyValue("header", out JsonNode? headerLow)
            && headerLow is JsonObject headerLowObj
            && headerLowObj.ContainsKey("subsystemType"))
            return true;

        // Uppercase Header.SubsystemType
        if (dom.TryGetPropertyValue("Header", out JsonNode? headerUp)
            && headerUp is JsonObject headerUpObj
            && headerUpObj.ContainsKey("SubsystemType"))
            return true;

        // Road network: top-level "nodes" array + "segments" array
        if (dom.TryGetPropertyValue("nodes", out JsonNode? nodesNode)
            && nodesNode is JsonArray
            && dom.TryGetPropertyValue("segments", out JsonNode? segmentsNode)
            && segmentsNode is JsonArray)
            return true;

        return false;
    }

    private static bool IsScenarioFixture(JsonObject dom)
    {
        if (dom.TryGetPropertyValue("header", out JsonNode? headerNode)
            && headerNode is JsonObject headerObj
            && headerObj.TryGetPropertyValue("subsystemType", out JsonNode? subType)
            && subType is JsonValue subTypeVal
            && string.Equals(subTypeVal.GetValue<string>(), HrotDocumentTypes.Scenario,
                StringComparison.Ordinal))
            return true;

        return false;
    }

    private static bool IsBlueprintFixture(JsonObject dom)
    {
        if (dom.TryGetPropertyValue("Header", out JsonNode? headerNode)
            && headerNode is JsonObject headerObj
            && headerObj.TryGetPropertyValue("SubsystemType", out JsonNode? subType)
            && subType is JsonValue subTypeVal
            && string.Equals(subTypeVal.GetValue<string>(), HrotDocumentTypes.Blueprint,
                StringComparison.Ordinal))
            return true;

        return false;
    }

    /// <summary>
    /// Returns true if the file path should be excluded from fixture scanning.
    /// Mirrors the exclusion logic in Fdp.Tools.EnvelopeStamper.FixtureStamper.ShouldSkipPath.
    /// </summary>
    private static bool ShouldSkipPath(string path)
    {
        if (path.Contains(@"\obj\") || path.Contains("/obj/")
            || path.Contains(@"\bin\") || path.Contains("/bin/"))
            return true;

        if (path.Contains(@"\ExtDeps\") || path.Contains("/ExtDeps/"))
            return true;

        if (path.Contains(@"\.tmp\") || path.Contains("/.tmp/"))
            return true;

        if (path.Contains(@"\.claude\") || path.Contains("/.claude/"))
            return true;

        if (path.Contains(@"Fdp.Core.Tests\Serialization\Migrations")
            || path.Contains("Fdp.Core.Tests/Serialization/Migrations"))
            return true;

        if (path.Contains(@"Navigation\data") || path.Contains("Navigation/data"))
            return true;

        string fileName = Path.GetFileName(path);

        if (string.Equals(fileName, "xunit.runner.json", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(fileName, "launchSettings.json", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(fileName, "settings.json", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(fileName, "settings.local.json", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fileName.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
