using System;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;
using Xunit;

namespace Fdp.Core.Tests.Serialization.Migrations;

/// <summary>
/// Tests for <see cref="MigrationPipeline"/> (T1-120 through T1-139).
/// </summary>
public sealed class MigrationPipelineTests
{
    // ---------------------------------------------------------------
    // Test infrastructure
    // ---------------------------------------------------------------

    // "Test.Doc" has three versions: v1 -> v2 -> v3.
    // Registry contains both up and down migrators for all steps.
    private static MigrationRegistry CreateDefaultRegistry()
    {
        var reg = new MigrationRegistry();
        reg.RegisterDocType("Test.Doc", currentVersion: 3, new IJsonDocumentMigrator[]
        {
            new TestDocV1ToV2(),
            new TestDocV2ToV1(),
            new TestDocV2ToV3(),
            new TestDocV3ToV2(),
        });
        return reg;
    }

    private static MigrationRegistry CreatePassthroughRegistry()
    {
        var reg = new MigrationRegistry();
        reg.RegisterPassthroughDocType("Stable.Doc", currentVersion: 1);
        return reg;
    }

    // Builds a minimal JSON object with $meta and an items array.
    private static JsonObject MakeDoc(int schemaVersion, string docType = "Test.Doc")
    {
        return JsonNode.Parse(
            $"{{\"$meta\":{{\"docType\":\"{docType}\",\"schemaVersion\":{schemaVersion}}}," +
            $"\"items\":[{{\"name\":\"a\"}}]}}")!.AsObject();
    }

    // Creates a registry with a single violating migrator (paired with a harmless stub).
    private static MigrationRegistry CreateRegistryWithBadMigrator(
        IJsonDocumentMigrator badMigrator,
        IJsonDocumentMigrator? pairedDownMigrator = null)
    {
        var reg = new MigrationRegistry();
        var paired = pairedDownMigrator
            ?? new TestDocV2ToV1();  // safe stub for v2->v1 step
        reg.RegisterDocType("Test.Doc", currentVersion: 2, new IJsonDocumentMigrator[]
        {
            badMigrator,
            paired,
        });
        return reg;
    }

    // ---------------------------------------------------------------
    // T1-120: Already at current version -> empty report
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateToCurrent_AlreadyCurrent_ReturnsEmptyReport()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(3);

        var report = pipeline.MigrateToCurrent(doc);

        Assert.Equal(0, report.Warnings.Count);
        Assert.Equal(0, report.Notes.Count);
        Assert.Equal(3, report.FromVersion);
        Assert.Equal(3, report.ToVersion);
    }

    // ---------------------------------------------------------------
    // T1-121: Single-step upgrade v1 -> v2
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateToCurrent_OneStepUp_ReturnsCorrectReport()
    {
        var reg = new MigrationRegistry();
        reg.RegisterDocType("Test.Doc", currentVersion: 2, new IJsonDocumentMigrator[]
        {
            new TestDocV1ToV2(),
            new TestDocV2ToV1(),
        });
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        var report = pipeline.MigrateToCurrent(doc);

        Assert.Equal(1, report.FromVersion);
        Assert.Equal(2, report.ToVersion);
    }

    // ---------------------------------------------------------------
    // T1-122: Multi-step upgrade v1 -> v3
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateToCurrent_MultiStepUp_ReturnsCorrectVersions()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        var report = pipeline.MigrateToCurrent(doc);

        Assert.Equal(1, report.FromVersion);
        Assert.Equal(3, report.ToVersion);
    }

    // ---------------------------------------------------------------
    // Downgrade v2 -> v1 (unlabeled extra test)
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_Downgrade_ReturnsDownReport()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(2);

        var report = pipeline.MigrateTo(doc, 1);

        Assert.Equal(2, report.FromVersion);
        Assert.Equal(1, report.ToVersion);
    }

    // ---------------------------------------------------------------
    // Passthrough doc type -> empty report (unlabeled extra test)
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateToCurrent_PassthroughDocType_ReturnsEmptyReport()
    {
        var reg = CreatePassthroughRegistry();
        var pipeline = new MigrationPipeline(reg);
        var doc = JsonNode.Parse(
            "{\"$meta\":{\"docType\":\"Stable.Doc\",\"schemaVersion\":1}}")!.AsObject();

        var report = pipeline.MigrateToCurrent(doc);

        Assert.Equal(0, report.Warnings.Count);
        Assert.Equal(0, report.Notes.Count);
    }

    // ---------------------------------------------------------------
    // Unknown doc type -> throws MigrationException (unlabeled extra test)
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateToCurrent_UnknownDocType_ThrowsMigrationException()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);
        var doc = JsonNode.Parse(
            "{\"$meta\":{\"docType\":\"Unknown.Doc\",\"schemaVersion\":1}}")!.AsObject();

        Assert.Throws<MigrationException>(() => pipeline.MigrateToCurrent(doc));
    }

    // ---------------------------------------------------------------
    // T1-126: MigrateTo same version -> empty report
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_SameVersion_ReturnsEmptyReport()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(2);

        var report = pipeline.MigrateTo(doc, 2);

        Assert.Equal(0, report.Warnings.Count);
        Assert.Equal(0, report.Notes.Count);
        Assert.Equal(2, report.FromVersion);
        Assert.Equal(2, report.ToVersion);
    }

    // ---------------------------------------------------------------
    // T1-127: After migration $meta.schemaVersion is updated correctly
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateToCurrent_SetsSchemaVersionInDocument()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        pipeline.MigrateToCurrent(doc);

        int finalVersion = doc["$meta"]!.AsObject()["schemaVersion"]!.GetValue<int>();
        Assert.Equal(3, finalVersion);
    }

    // ---------------------------------------------------------------
    // T1-128: Apply is called on every migrator in the chain
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateToCurrent_CallsEachMigratorApply()
    {
        var v1to2 = new TestDocV1ToV2();
        var v2to1 = new TestDocV2ToV1();
        var v2to3 = new TestDocV2ToV3();
        var v3to2 = new TestDocV3ToV2();

        var reg = new MigrationRegistry();
        reg.RegisterDocType("Test.Doc", currentVersion: 3, new IJsonDocumentMigrator[]
        {
            v1to2, v2to1, v2to3, v3to2,
        });
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        pipeline.MigrateToCurrent(doc);

        // After v1->v3 migration, each item should have both "kind" and "metadata".
        var item = doc["items"]![0]!.AsObject();
        Assert.True(item.ContainsKey("kind"));
        Assert.True(item.ContainsKey("metadata"));
    }

    // ---------------------------------------------------------------
    // Report.Direction is Up for upgrade (unlabeled extra test)
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_Upgrade_DirectionIsUp()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        var report = pipeline.MigrateTo(doc, 2);

        Assert.Equal(MigrationDirection.Up, report.Direction);
    }

    // ---------------------------------------------------------------
    // T1-130: Migrator changes docType -> MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_MigratorChangesDocType_ThrowsMigrationException()
    {
        var reg = CreateRegistryWithBadMigrator(new TestDocV1ToV2_ChangesDocType());
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        var ex = Assert.Throws<MigrationException>(() => pipeline.MigrateTo(doc, 2));
        Assert.Contains("docType", ex.Message);
    }

    // ---------------------------------------------------------------
    // T1-131: Migrator replaces $meta object -> MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_MigratorReplacesMetaObject_ThrowsMigrationException()
    {
        var reg = CreateRegistryWithBadMigrator(new TestDocV1ToV2_ReplacesMetaObject());
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        var ex = Assert.Throws<MigrationException>(() => pipeline.MigrateTo(doc, 2));
        Assert.Contains("$meta", ex.Message);
    }

    // ---------------------------------------------------------------
    // T1-132: Migrator changes schemaVersion -> MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_MigratorChangesSchemaVersion_ThrowsMigrationException()
    {
        var reg = CreateRegistryWithBadMigrator(new TestDocV1ToV2_ChangesSchemaVersion());
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        var ex = Assert.Throws<MigrationException>(() => pipeline.MigrateTo(doc, 2));
        Assert.Contains("schemaVersion", ex.Message);
    }

    // ---------------------------------------------------------------
    // T1-133: Migrator throws non-MigrationException -> wrapped
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_MigratorThrowsNonMigrationException_IsWrapped()
    {
        var reg = CreateRegistryWithBadMigrator(new TestDocV1ToV2_Throws());
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        var ex = Assert.Throws<MigrationException>(() => pipeline.MigrateTo(doc, 2));
        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    // ---------------------------------------------------------------
    // T1-134: Migrator adds warning -> report contains it
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_MigratorAddsWarning_ReportContainsWarning()
    {
        var reg = CreateRegistryWithBadMigrator(new TestDocV1ToV2_WithWarning());
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        var report = pipeline.MigrateTo(doc, 2);

        Assert.Single(report.Warnings);
        Assert.Equal("test warning", report.Warnings[0].Message);
    }

    // ---------------------------------------------------------------
    // T1-135: Migrator adds note -> report contains it
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_MigratorAddsNote_ReportContainsNote()
    {
        var reg = CreateRegistryWithBadMigrator(new TestDocV1ToV2_WithNote());
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        var report = pipeline.MigrateTo(doc, 2);

        Assert.Single(report.Notes);
        Assert.Equal("test note", report.Notes[0]);
    }

    // ---------------------------------------------------------------
    // Direction is Down for downgrade (unlabeled extra test)
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_Downgrade_DirectionIsDown()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(3);

        var report = pipeline.MigrateTo(doc, 1);

        Assert.Equal(MigrationDirection.Down, report.Direction);
    }

    // ---------------------------------------------------------------
    // T1-137: Multi-step downgrade works
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_MultiStepDown_DocumentTransformedCorrectly()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);
        // Build a v3 document that has "kind" and "metadata" on each item.
        var doc = JsonNode.Parse(
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":3}," +
            "\"items\":[{\"name\":\"a\",\"kind\":\"default\",\"metadata\":{}}]}")!.AsObject();

        pipeline.MigrateTo(doc, 1);

        var item = doc["items"]![0]!.AsObject();
        Assert.False(item.ContainsKey("kind"));
        Assert.False(item.ContainsKey("metadata"));
        Assert.Equal(1, doc["$meta"]!.AsObject()["schemaVersion"]!.GetValue<int>());
    }

    // ---------------------------------------------------------------
    // T1-138: Duration > TimeSpan.Zero after actual migration
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_WithMigratorsRun_DurationIsPositive()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        var report = pipeline.MigrateTo(doc, 2);

        Assert.True(report.Duration > TimeSpan.Zero);
    }

    // ---------------------------------------------------------------
    // T1-139: Warning path is captured via ctx.WithItem scope
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_WarningInsideWithItemScope_CapturesPath()
    {
        var reg = CreateRegistryWithBadMigrator(new TestDocV1ToV2_WarningWithPath());
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        var report = pipeline.MigrateTo(doc, 2);

        Assert.Single(report.Warnings);
        Assert.Equal("$.items", report.Warnings[0].Path);
    }

    // ---------------------------------------------------------------
    // T1-123: engineVersion is preserved after migration
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateToCurrent_PreservesEngineVersionField()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);
        var doc = JsonNode.Parse(
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1," +
            "\"engineVersion\":\"1.2.3\",\"createdUtc\":\"2026-01-01T00:00:00Z\"," +
            "\"createdBy\":\"TestUser\"},\"items\":[]}")!.AsObject();

        pipeline.MigrateToCurrent(doc);

        var meta = doc["$meta"]!.AsObject();
        Assert.Equal("1.2.3", meta["engineVersion"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T1-124: createdUtc is preserved after migration
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateToCurrent_PreservesCreatedUtcField()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);
        var doc = JsonNode.Parse(
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1," +
            "\"engineVersion\":\"1.2.3\",\"createdUtc\":\"2026-01-01T00:00:00Z\"," +
            "\"createdBy\":\"TestUser\"},\"items\":[]}")!.AsObject();

        pipeline.MigrateToCurrent(doc);

        var meta = doc["$meta"]!.AsObject();
        Assert.Equal("2026-01-01T00:00:00Z", meta["createdUtc"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T1-125: createdBy is preserved after migration
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateToCurrent_PreservesCreatedByField()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);
        var doc = JsonNode.Parse(
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1," +
            "\"engineVersion\":\"1.2.3\",\"createdUtc\":\"2026-01-01T00:00:00Z\"," +
            "\"createdBy\":\"TestUser\"},\"items\":[]}")!.AsObject();

        pipeline.MigrateToCurrent(doc);

        var meta = doc["$meta"]!.AsObject();
        Assert.Equal("TestUser", meta["createdBy"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T1-129: Chain halts on first failure; step 3 migrator must not be called
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateToCurrent_MigratorThrowsAtStep2of3_DoesNotRunStep3()
    {
        var step3up = new StubMigrator("Test.Doc", 3, 4);
        var reg = new MigrationRegistry();
        reg.RegisterDocType("Test.Doc", currentVersion: 4, new IJsonDocumentMigrator[]
        {
            new TestDocV1ToV2(), new TestDocV2ToV1(),
            new ThrowingMigratorV2ToV3(), new StubMigrator("Test.Doc", 3, 2),
            step3up, new StubMigrator("Test.Doc", 4, 3),
        });
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        Assert.Throws<MigrationException>(() => pipeline.MigrateToCurrent(doc));

        Assert.Equal(0, step3up.ApplyCallCount);
    }

    // ---------------------------------------------------------------
    // T1-136: MigrateTo a version not reachable in the chain throws
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_NoPathExists_Throws()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        Assert.Throws<MigrationException>(() => pipeline.MigrateTo(doc, 99));
    }

    // ---------------------------------------------------------------
    // T1-140: MigrateToCurrent with null root throws ArgumentNullException
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateToCurrent_NullRoot_ThrowsArgumentNullException()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);

        Assert.Throws<ArgumentNullException>(() => pipeline.MigrateToCurrent(null!));
    }

    // ---------------------------------------------------------------
    // T1-141: MigrateTo with null root throws ArgumentNullException
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_NullRoot_ThrowsArgumentNullException()
    {
        var reg = CreateDefaultRegistry();
        var pipeline = new MigrationPipeline(reg);

        Assert.Throws<ArgumentNullException>(() => pipeline.MigrateTo(null!, 2));
    }

    // ---------------------------------------------------------------
    // T1-142: Migrator changes engineVersion (invariant 4) -> MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_DiagnosticFields_EngineVersionChanged_ThrowsMigrationException()
    {
        var reg = CreateRegistryWithBadMigrator(new TestDocV1ToV2_ChangesEngineVersion());
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        var ex = Assert.Throws<MigrationException>(() => pipeline.MigrateTo(doc, 2));
        Assert.Contains("invariant 4", ex.Message);
    }

    // ---------------------------------------------------------------
    // T1-143: Migrator changes createdBy (invariant 4) -> MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void MigrateTo_DiagnosticFields_CreatedByChanged_ThrowsMigrationException()
    {
        var reg = CreateRegistryWithBadMigrator(new TestDocV1ToV2_ChangesCreatedBy());
        var pipeline = new MigrationPipeline(reg);
        var doc = MakeDoc(1);

        var ex = Assert.Throws<MigrationException>(() => pipeline.MigrateTo(doc, 2));
        Assert.Contains("invariant 4", ex.Message);
    }
}
