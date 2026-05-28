using System;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Internal;
using Xunit;

namespace Fdp.Core.Tests.Serialization.Migrations;

/// <summary>
/// Tests for <see cref="UnknownsJournal"/> (T1-260..T1-273).
/// </summary>
public sealed class UnknownsJournalTests
{
    // Helper: build minimal pre/post DOMs for a lossless round-trip.
    private static (JsonObject pre, JsonObject post) MakeLosslessPair()
    {
        var pre = JsonNode.Parse("{\"a\":1,\"b\":\"hello\"}")!.AsObject();
        var post = JsonNode.Parse("{\"a\":1,\"b\":\"hello\"}")!.AsObject();
        return (pre, post);
    }

    // Helper: build pre/post DOMs where post is missing a field (lossy).
    private static (JsonObject pre, JsonObject post) MakeLossyPair()
    {
        var pre = JsonNode.Parse("{\"a\":1,\"b\":\"hello\",\"c\":99}")!.AsObject();
        var post = JsonNode.Parse("{\"a\":1,\"b\":\"hello\"}")!.AsObject();
        return (pre, post);
    }

    // Helper: create a journal via Deserialize from a known JSON string
    private static UnknownsJournal MakeJournalViaJson(string opsJson)
    {
        var json = "{" +
            "\"$meta\":{\"docType\":\"Fdp.MigrationJournal\",\"schemaVersion\":1," +
            "\"engineVersion\":\"1.0\",\"createdBy\":\"Test\"}," +
            "\"sourceDocType\":\"Test.Doc\"," +
            "\"sourceFileVersion\":2," +
            "\"downMigratedToVersion\":1," +
            "\"sourceContentHash\":\"abcdef1234567890\"," +
            $"\"operations\":[{opsJson}]" +
            "}";
        return UnknownsJournal.Deserialize(json);
    }

    // ---------------------------------------------------------------
    // T1-260: Identical DOMs -> empty operations
    // ---------------------------------------------------------------
    [Fact]
    public void Compute_LosslessRoundTrip_ReturnsEmptyOperations()
    {
        var (pre, post) = MakeLosslessPair();

        var journal = UnknownsJournal.Compute(pre, post,
            "Test.Doc", 2, 1, "hash123", "1.0", "TestTool");

        Assert.Empty(journal.Operations);
    }

    // ---------------------------------------------------------------
    // T1-261: Lossy round-trip -> correct Set operations
    // ---------------------------------------------------------------
    [Fact]
    public void Compute_LossyRoundTrip_ReturnsCorrectOperations()
    {
        var (pre, post) = MakeLossyPair();

        var journal = UnknownsJournal.Compute(pre, post,
            "Test.Doc", 2, 1, "hash123", "1.0", "TestTool");

        Assert.Single(journal.Operations);
        Assert.Equal(JournalOpKind.Set, journal.Operations[0].Kind);
        Assert.Equal("$.c", journal.Operations[0].Path);
        Assert.Equal(99, journal.Operations[0].Value!.GetValue<int>());
    }

    // ---------------------------------------------------------------
    // T1-262: Compute populates metadata fields correctly
    // ---------------------------------------------------------------
    [Fact]
    public void Compute_PopulatesMetadata()
    {
        var (pre, post) = MakeLosslessPair();

        var journal = UnknownsJournal.Compute(pre, post,
            "My.DocType", 5, 3, "myhash000", "2.0.1", "MyTool");

        Assert.Equal("My.DocType", journal.SourceDocType);
        Assert.Equal(5, journal.SourceFileVersion);
        Assert.Equal(3, journal.DownMigratedToVersion);
        Assert.Equal("myhash000", journal.SourceContentHash);
    }

    // ---------------------------------------------------------------
    // T1-263: Compute populates JournalMeta as "Fdp.MigrationJournal" v1
    // ---------------------------------------------------------------
    [Fact]
    public void Compute_PopulatesJournalEnvelope()
    {
        var (pre, post) = MakeLosslessPair();

        var journal = UnknownsJournal.Compute(pre, post,
            "Test.Doc", 2, 1, "h", "1.0", "Tool");

        Assert.Equal(FdpDocumentTypes.MigrationJournal, journal.JournalMeta.DocType);
        Assert.Equal(1, journal.JournalMeta.SchemaVersion);
    }

    // ---------------------------------------------------------------
    // T1-264: Serialize then Deserialize yields identical journal
    // ---------------------------------------------------------------
    [Fact]
    public void Serialize_RoundTripsThroughDeserialize()
    {
        var (pre, post) = MakeLossyPair();
        var original = UnknownsJournal.Compute(pre, post,
            "Test.Doc", 2, 1, "abc123", "1.0", "Tool");

        var json = original.Serialize();
        var restored = UnknownsJournal.Deserialize(json);

        Assert.Equal(original.SourceDocType, restored.SourceDocType);
        Assert.Equal(original.SourceFileVersion, restored.SourceFileVersion);
        Assert.Equal(original.DownMigratedToVersion, restored.DownMigratedToVersion);
        Assert.Equal(original.SourceContentHash, restored.SourceContentHash);
        Assert.Equal(original.Operations.Count, restored.Operations.Count);
        Assert.Equal(original.Operations[0].Kind, restored.Operations[0].Kind);
        Assert.Equal(original.Operations[0].Path, restored.Operations[0].Path);
        // D-012: verify Set operation value survives round-trip
        Assert.Equal(99, restored.Operations[0].Value!.GetValue<int>());
    }

    // ---------------------------------------------------------------
    // T1-265: Deserialize valid JSON -> returns instance
    // ---------------------------------------------------------------
    [Fact]
    public void Deserialize_ValidJournal_ReturnsInstance()
    {
        var json =
            "{\"$meta\":{\"docType\":\"Fdp.MigrationJournal\",\"schemaVersion\":1}," +
            "\"sourceDocType\":\"Hrot.Scenario\"," +
            "\"sourceFileVersion\":4," +
            "\"downMigratedToVersion\":3," +
            "\"sourceContentHash\":\"b7c1d9e2f4a86075\"," +
            "\"operations\":[]}";

        var journal = UnknownsJournal.Deserialize(json);

        Assert.Equal("Hrot.Scenario", journal.SourceDocType);
        Assert.Equal(4, journal.SourceFileVersion);
        Assert.Equal(3, journal.DownMigratedToVersion);
        Assert.Equal("b7c1d9e2f4a86075", journal.SourceContentHash);
        Assert.Empty(journal.Operations);
    }

    // ---------------------------------------------------------------
    // T1-266: Wrong docType -> throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void Deserialize_WrongDocType_Throws()
    {
        var json =
            "{\"$meta\":{\"docType\":\"Hrot.Scenario\",\"schemaVersion\":1}," +
            "\"sourceDocType\":\"X\"," +
            "\"sourceFileVersion\":1," +
            "\"downMigratedToVersion\":1," +
            "\"sourceContentHash\":\"abc\"," +
            "\"operations\":[]}";

        Assert.Throws<MigrationException>(() => UnknownsJournal.Deserialize(json));
    }

    // ---------------------------------------------------------------
    // T1-267: Missing sourceContentHash -> throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void Deserialize_MissingFields_Throws()
    {
        var json =
            "{\"$meta\":{\"docType\":\"Fdp.MigrationJournal\",\"schemaVersion\":1}," +
            "\"sourceDocType\":\"X\"," +
            "\"sourceFileVersion\":1," +
            "\"downMigratedToVersion\":1," +
            "\"operations\":[]}";

        // sourceContentHash is missing
        Assert.Throws<MigrationException>(() => UnknownsJournal.Deserialize(json));
    }

    // ---------------------------------------------------------------
    // T1-268: ApplyTo Set op with existing parent -> sets value
    // ---------------------------------------------------------------
    [Fact]
    public void ApplyTo_SetOpExistingParent_Sets()
    {
        var journal = MakeJournalViaJson("{\"kind\":\"Set\",\"path\":\"$.x\",\"value\":42}");
        var dom = JsonNode.Parse("{\"x\":0}")!.AsObject();

        journal.ApplyTo(dom);

        Assert.Equal(42, dom["x"]!.GetValue<int>());
    }

    // ---------------------------------------------------------------
    // T1-269: ApplyTo Set op with missing parent -> skips (user-deletion-wins)
    // ---------------------------------------------------------------
    [Fact]
    public void ApplyTo_SetOpMissingParent_Skips()
    {
        // $.missing.field: the parent "missing" does not exist
        var journal = MakeJournalViaJson(
            "{\"kind\":\"Set\",\"path\":\"$.missing.field\",\"value\":99}");
        var dom = JsonNode.Parse("{\"a\":1}")!.AsObject();

        // Must not throw; must leave dom unchanged
        journal.ApplyTo(dom);

        Assert.Null(dom["missing"]);
    }

    // ---------------------------------------------------------------
    // T1-270: ApplyTo Remove op on existing path -> removes
    // ---------------------------------------------------------------
    [Fact]
    public void ApplyTo_RemoveOpExistingPath_Removes()
    {
        var journal = MakeJournalViaJson("{\"kind\":\"Remove\",\"path\":\"$.x\"}");
        var dom = JsonNode.Parse("{\"x\":1,\"y\":2}")!.AsObject();

        journal.ApplyTo(dom);

        Assert.False(dom.ContainsKey("x"));
        Assert.True(dom.ContainsKey("y"));
    }

    // ---------------------------------------------------------------
    // T1-271: ApplyTo Remove op on missing path -> no-op (idempotent)
    // ---------------------------------------------------------------
    [Fact]
    public void ApplyTo_RemoveOpMissingPath_NoOp()
    {
        var journal = MakeJournalViaJson("{\"kind\":\"Remove\",\"path\":\"$.absent\"}");
        var dom = JsonNode.Parse("{\"a\":1}")!.AsObject();

        // Must not throw; dom unchanged
        journal.ApplyTo(dom);

        Assert.True(dom.ContainsKey("a"));
    }

    // ---------------------------------------------------------------
    // T1-272: Set then Remove on same path -> Remove wins (path absent)
    // ---------------------------------------------------------------
    [Fact]
    public void ApplyTo_SetThenRemoveSamePath_RemoveWins()
    {
        // Operations in journal order: Set("$.x", 5), Remove("$.x")
        // Apply order: all Sets first -> x=5, then all Removes -> x absent
        var journal = MakeJournalViaJson(
            "{\"kind\":\"Set\",\"path\":\"$.x\",\"value\":5}," +
            "{\"kind\":\"Remove\",\"path\":\"$.x\"}");
        var dom = JsonNode.Parse("{\"x\":0}")!.AsObject();

        journal.ApplyTo(dom);

        Assert.False(dom.ContainsKey("x"));
    }

    // ---------------------------------------------------------------
    // T1-273: Operations applied in Set-first-then-Remove order
    // ---------------------------------------------------------------
    [Fact]
    public void ApplyTo_OperationsAppliedSetFirstThenRemove_PerOrder()
    {
        // Journal ops in REVERSED order: Remove("$.x") listed first, Set("$.x",99) second.
        // If applied in journal order: Remove first -> x gone, Set after -> x=99.
        // If applied in Set-first order: Set first -> x=99, Remove after -> x absent.
        // Spec mandates Set-first, so x must be absent after apply.
        var journal = MakeJournalViaJson(
            "{\"kind\":\"Remove\",\"path\":\"$.x\"}," +
            "{\"kind\":\"Set\",\"path\":\"$.x\",\"value\":99}");
        var dom = JsonNode.Parse("{\"x\":1}")!.AsObject();

        journal.ApplyTo(dom);

        // Set-first order: Set(x=99) then Remove(x) -> x absent
        Assert.False(dom.ContainsKey("x"));
    }

    // ---------------------------------------------------------------
    // T1-274: Deserialize invalid JSON string -> throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void Deserialize_InvalidJson_Throws()
    {
        Assert.Throws<MigrationException>(() =>
            UnknownsJournal.Deserialize("not valid json {{{"));
    }

    // ---------------------------------------------------------------
    // T1-275: Deserialize array root -> throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void Deserialize_ArrayRoot_Throws()
    {
        Assert.Throws<MigrationException>(() =>
            UnknownsJournal.Deserialize("[1, 2, 3]"));
    }

    // ---------------------------------------------------------------
    // T1-276: Deserialize missing $meta -> throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void Deserialize_MissingMeta_Throws()
    {
        var json =
            "{\"sourceDocType\":\"Test.Doc\"," +
            "\"sourceFileVersion\":2," +
            "\"downMigratedToVersion\":1," +
            "\"sourceContentHash\":\"abc\"," +
            "\"operations\":[]}";

        Assert.Throws<MigrationException>(() => UnknownsJournal.Deserialize(json));
    }

    // ---------------------------------------------------------------
    // T1-277: Deserialize missing schemaVersion -> throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void Deserialize_MissingSchemaVersion_Throws()
    {
        var json =
            "{\"$meta\":{\"docType\":\"Fdp.MigrationJournal\"}," +
            "\"sourceDocType\":\"Test.Doc\"," +
            "\"sourceFileVersion\":2," +
            "\"downMigratedToVersion\":1," +
            "\"sourceContentHash\":\"abc\"," +
            "\"operations\":[]}";

        Assert.Throws<MigrationException>(() => UnknownsJournal.Deserialize(json));
    }

    // ---------------------------------------------------------------
    // T1-278: Deserialize wrong schemaVersion -> throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void Deserialize_WrongSchemaVersion_Throws()
    {
        var json =
            "{\"$meta\":{\"docType\":\"Fdp.MigrationJournal\",\"schemaVersion\":99}," +
            "\"sourceDocType\":\"Test.Doc\"," +
            "\"sourceFileVersion\":2," +
            "\"downMigratedToVersion\":1," +
            "\"sourceContentHash\":\"abc\"," +
            "\"operations\":[]}";

        Assert.Throws<MigrationException>(() => UnknownsJournal.Deserialize(json));
    }

    // ---------------------------------------------------------------
    // T1-279: Deserialize missing operations array -> throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void Deserialize_MissingOperationsArray_Throws()
    {
        var json =
            "{\"$meta\":{\"docType\":\"Fdp.MigrationJournal\",\"schemaVersion\":1}," +
            "\"sourceDocType\":\"Test.Doc\"," +
            "\"sourceFileVersion\":2," +
            "\"downMigratedToVersion\":1," +
            "\"sourceContentHash\":\"abc\"}";

        Assert.Throws<MigrationException>(() => UnknownsJournal.Deserialize(json));
    }

    // ---------------------------------------------------------------
    // T1-280: Deserialize unknown operation kind -> throws MigrationException
    // ---------------------------------------------------------------
    [Fact]
    public void Deserialize_UnknownOperationKind_Throws()
    {
        var json =
            "{\"$meta\":{\"docType\":\"Fdp.MigrationJournal\",\"schemaVersion\":1}," +
            "\"sourceDocType\":\"Test.Doc\"," +
            "\"sourceFileVersion\":2," +
            "\"downMigratedToVersion\":1," +
            "\"sourceContentHash\":\"abc\"," +
            "\"operations\":[{\"kind\":\"Upsert\",\"path\":\"$.x\"}]}";

        Assert.Throws<MigrationException>(() => UnknownsJournal.Deserialize(json));
    }

    // ---------------------------------------------------------------
    // T1-281: Deserialize journal with no optional meta fields (no engineVersion,
    //         createdBy, createdUtc), then Serialize omits those fields.
    //         Covers the FALSE branches in Serialize's optional-field checks
    //         and the FALSE branch of the createdUtc presence check in Deserialize.
    // ---------------------------------------------------------------
    [Fact]
    public void Serialize_WithNullOptionalFields_OmitsOptionalFieldsFromOutput()
    {
        // Build a minimal journal JSON with no optional meta fields.
        var minimalJson =
            "{\"$meta\":{\"docType\":\"Fdp.MigrationJournal\",\"schemaVersion\":1}," +
            "\"sourceDocType\":\"Test.Doc\"," +
            "\"sourceFileVersion\":3," +
            "\"downMigratedToVersion\":2," +
            "\"sourceContentHash\":\"deadbeef0000cafe\"," +
            "\"operations\":[{\"kind\":\"Remove\",\"path\":\"$.extra\"}]}";

        var journal = UnknownsJournal.Deserialize(minimalJson);

        // JournalMeta optional fields should be null (no engineVersion, createdBy, createdUtc).
        Assert.Null(journal.JournalMeta.EngineVersion);
        Assert.Null(journal.JournalMeta.CreatedBy);
        Assert.False(journal.JournalMeta.CreatedUtc.HasValue);

        // Re-serialize: optional fields must not appear in output.
        var serialized = journal.Serialize();
        Assert.DoesNotContain("engineVersion", serialized);
        Assert.DoesNotContain("createdBy", serialized);
        Assert.DoesNotContain("createdUtc", serialized);
    }
}
