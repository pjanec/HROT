using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;
using Hrot.Blueprints.Core;
using Hrot.Common.Scenario;
using Hrot.Common.Scenario.Migrations;

namespace Hrot.Blueprints.Tests.Golden;

/// <summary>
/// ⭐⭐ <b><c>U-10</c> step 2 — the transform through the REGISTRY, not called directly.</b>
///
/// <para>
/// ⛔ <b>"The migrator is registered" is not "the migrator runs", and neither is "the transform
/// passes".</b> <c>BlueprintSchemaV2Tests</c> proves the transform in isolation; this proves what the
/// <b>pipeline</b> hands it and what it hands back. The two differ in ways that are invisible from
/// either side alone — the pipeline owns <c>$meta</c> and enforces four invariants around every
/// <c>Apply</c>, and the transform <b>rebuilds</b> its document rather than mutating it.
/// </para>
///
/// <para>
/// ⚠⚠ <b>Invariant 1 is the one that bites:</b> <c>MigrationPipeline</c> asserts
/// <c>ReferenceEquals</c> on the <c>$meta</c> <b>object instance</b> across <c>Apply</c>. A migrator
/// that rebuilt the root — which is exactly what wrapping <c>Up</c> naively would do — fails it.
/// </para>
/// </summary>
public sealed class BlueprintMigrationRegistryTests
{
    private static readonly System.Text.Json.JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// ⭐ The registry built by <c>BlueprintMigrationModule.RegisterAll</c> itself — the exact
    /// registration the five host profiles perform — with a pipeline over it. ⚠ Deliberately not the
    /// full <c>MigrationServices</c>: the storage and adapters are not what step 2 changed, and
    /// depending on them would make this test fail for reasons that are not about the migrator.
    /// </summary>
    private static MigrationPipeline Pipeline()
    {
        var registry = new MigrationRegistry();
        BlueprintMigrationModule.RegisterAll(registry);
        return new MigrationPipeline(registry);
    }

    private static MigrationRegistry Registry()
    {
        var registry = new MigrationRegistry();
        BlueprintMigrationModule.RegisterAll(registry);
        return registry;
    }

    private static JsonObject WithMeta(JsonObject body, int version)
    {
        var root = new JsonObject
        {
            ["$meta"] = new JsonObject
            {
                ["docType"]       = HrotDocumentTypes.Blueprint,
                ["schemaVersion"] = version,
            },
        };
        foreach (var p in body) root[p.Key] = p.Value?.DeepClone();
        return root;
    }

    private static JsonObject Body(string file)
    {
        var dom = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        dom.Remove("$meta");
        return dom;
    }

    private static IEnumerable<string> Files() => CorpusCanonicalisationTests.AllManagedFiles();

    // ── the Scenario module's three, mirrored ───────────────────────────────

    [Fact]
    public void CurrentVersion_Is2()
        => Assert.Equal(2, BlueprintMigrationModule.CurrentVersion);

    [Fact]
    public void CanMigrateV1ToV2()
    {
        Assert.True(Registry().CanMigrate(HrotDocumentTypes.Blueprint, 1, 2));
    }

    [Fact]
    public void CanMigrateV2ToV1()
    {
        Assert.True(Registry().CanMigrate(HrotDocumentTypes.Blueprint, 2, 1));
    }

    /// <summary>
    /// ⛔ <b>Not a passthrough</b> — the distinction the whole ordering rests on.
    /// <c>MigrationPipeline.MigrateTo</c> returns from the passthrough arm <b>before</b> the
    /// <c>fromVersion == targetVersion</c> comparison, so a passthrough registered at 2 would mean no
    /// transform ever runs while <c>CurrentVersion</c> advertises 2 — every v1 file silently never
    /// visited.
    /// </summary>
    [Fact]
    public void TheBlueprintDocTypeIsNotRegisteredAsAPassthrough()
    {
        Assert.False(Registry().IsPassthrough(HrotDocumentTypes.Blueprint));
    }

    // ── the real thing: all 58, through the pipeline ────────────────────────

    /// <summary>
    /// ⭐⭐ <b><c>v1 → v2 → v1</c> byte-identical on all 58, driven by the PIPELINE.</b> Batch 49
    /// proved this against a direct call; step 2 changed what invokes it, so it is re-proved here.
    /// ⭐ Also the only place that exercises <b>the revert</b> — the down-migrator is what puts a
    /// v2 file outside this repo back, and <c>git revert</c> cannot.
    /// </summary>
    [Fact]
    public void EveryShippedAssetRoundTripsThroughThePipeline()
    {
        var pipeline = Pipeline();
        var broken = new List<string>();

        foreach (var file in Files())
        {
            var v1       = WithMeta(Body(file), 1);
            var original = v1.ToJsonString(Indented);

            pipeline.MigrateTo(v1, 2, file);
            Assert.Equal(2, v1["$meta"]!["schemaVersion"]!.GetValue<int>());
            Assert.True(BlueprintSchemaV2.IsV2(v1), $"{Path.GetFileName(file)} is not v2 after up");

            pipeline.MigrateTo(v1, 1, file);

            if (!string.Equals(original, v1.ToJsonString(Indented), StringComparison.Ordinal))
                broken.Add(Path.GetFileName(file));
        }

        Assert.True(broken.Count == 0,
            "v1 -> v2 -> v1 through the registry chain is not the identity for:\n  "
            + string.Join("\n  ", broken));
    }

    /// <summary>
    /// ⭐ <b>The pipeline's own <c>$meta</c> invariants hold.</b> ⚠ Invariant 1 compares the object
    /// INSTANCE, so this asserts identity rather than equality — the migrators detach and re-attach
    /// that instance precisely to satisfy it, and an ordinary deep-copy would pass every other
    /// assertion in this file while failing here.
    /// </summary>
    [Fact]
    public void TheMetaInstanceSurvivesTheMigration()
    {
        var pipeline = Pipeline();
        var v1     = WithMeta(Body(Files().First()), 1);
        var before = v1["$meta"];

        pipeline.MigrateTo(v1, 2, "identity-probe");

        Assert.Same(before, v1["$meta"]);
        Assert.Equal(HrotDocumentTypes.Blueprint, v1["$meta"]!["docType"]!.GetValue<string>());
    }

    // ── Batch 54's nine fixtures, through the registry ──────────────────────

    private const string Decl = @"{ ""Id"": ""11111111-1111-1111-1111-111111111111"", ""Name"": ""X"" }";

    private static JsonObject Fixture(string body) => WithMeta(JsonNode.Parse(body)!.AsObject(), 1);

    /// <summary>
    /// ⭐⭐ <b>The four refusals still refuse through the pipeline</b> — and, importantly, arrive as a
    /// <c>MigrationException</c> naming the migrator and the file rather than as a raw
    /// <c>InvalidDataException</c>. ⛔ That wrapping is what makes <c>--mode migrate</c>'s per-file
    /// catch able to report which asset failed.
    /// </summary>
    [Theory]
    [InlineData(@"{ ""Name"": ""NoWorkingState"", ""Parameters"": [], ""Variables"": [] }", "WorkingState")]
    [InlineData(@"{ ""Name"": ""NullList"", ""Parameters"": null, ""WorkingState"": [], ""Variables"": [] }", "Parameters")]
    [InlineData(@"{ ""Name"": ""OutOfOrder"", ""Variables"": [], ""Parameters"": [], ""WorkingState"": [] }", "order")]
    [InlineData(@"{ ""Name"": ""KindClash"", ""Parameters"": [], ""WorkingState"": [],
                    ""Variables"": [{ ""Id"": ""55555555-5555-5555-5555-555555555555"", ""Kind"": ""Parameter"" }] }", "Kind")]
    public void TheFourRefusedShapesAreRefusedThroughThePipelineToo(string body, string mustMention)
    {
        var pipeline = Pipeline();
        var ex = Assert.Throws<MigrationException>(
            () => pipeline.MigrateTo(Fixture(body), 2, "refusal-probe"));

        Assert.Contains(mustMention, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⭐ <b>And the four pinned survivors still survive</b> — a refusal that widened would be just as
    /// much a regression as one that narrowed. ⚠ The cross-kind collision is the one worth naming:
    /// <c>BP1673</c> refuses it at Stage 2, and the migrator must still read it, or it cannot be used
    /// to fix the assets that do not compile.
    /// </summary>
    [Theory]
    [InlineData(@"{ ""Name"": ""Empty"", ""Parameters"": [], ""WorkingState"": [], ""Variables"": [] }")]
    [InlineData(@"{ ""Name"": ""StaleOrder"", ""Parameters"": [], ""WorkingState"": [],
                    ""Variables"": [" + Decl + @"], ""VariableOrder"": [""99999999-9999-9999-9999-999999999999""] }")]
    [InlineData(@"{ ""Name"": ""Collision"", ""Parameters"": [],
                    ""WorkingState"": [{ ""Id"": ""22222222-2222-2222-2222-222222222222"", ""Name"": ""Health"" }],
                    ""Variables"": [{ ""Id"": ""33333333-3333-3333-3333-333333333333"", ""Name"": ""Health"" }] }")]
    [InlineData(@"{ ""Name"": ""Unknown"", ""Parameters"": [], ""WorkingState"": [],
                    ""Variables"": [{ ""Id"": ""44444444-4444-4444-4444-444444444444"", ""FutureThing"": 7 }] }")]
    public void TheFourPinnedSurvivorsRoundTripThroughThePipeline(string body)
    {
        var pipeline = Pipeline();
        var root     = Fixture(body);
        var original = root.ToJsonString(Indented);

        pipeline.MigrateTo(root, 2, "survivor-probe");
        Assert.True(BlueprintSchemaV2.IsV2(root));

        pipeline.MigrateTo(root, 1, "survivor-probe");
        Assert.Equal(original, root.ToJsonString(Indented));
    }
}
