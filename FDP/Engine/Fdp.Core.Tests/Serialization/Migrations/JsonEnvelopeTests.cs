using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;

namespace Fdp.Core.Tests.Serialization.Migrations;

public class JsonEnvelopeTests
{
    // ---------------------------------------------------------------
    // Peek overloads — happy paths
    // ---------------------------------------------------------------

    // T1-001: String overload returns correct DocumentMeta from valid envelope.
    [Fact]
    public void Peek_StringInput_ReturnsParsedMeta()
    {
        string path = TestFixtureLoader.GetPath("Envelopes/valid_full.json");
        var meta = JsonEnvelope.Peek(path);

        Assert.Equal("Test.Doc", meta.DocType);
        Assert.Equal(2, meta.SchemaVersion);
        Assert.Equal("1.0.0", meta.EngineVersion);
        Assert.Equal("TestTool", meta.CreatedBy);
        Assert.NotNull(meta.CreatedUtc);
    }

    // T1-002: ReadOnlySpan<byte> overload works identically to string overload.
    [Fact]
    public void Peek_ByteSpanInput_ReturnsParsedMeta()
    {
        byte[] bytes = TestFixtureLoader.LoadBytes("Envelopes/valid_full.json");
        var meta = JsonEnvelope.Peek(bytes.AsSpan());

        Assert.Equal("Test.Doc", meta.DocType);
        Assert.Equal(2, meta.SchemaVersion);
    }

    // T1-003: Stream overload works identically; stream position advances but stream is not disposed.
    [Fact]
    public void Peek_StreamInput_ReturnsParsedMeta()
    {
        using var stream = TestFixtureLoader.OpenStream("Envelopes/valid_full.json");
        var meta = JsonEnvelope.Peek(stream);

        Assert.Equal("Test.Doc", meta.DocType);
        Assert.Equal(2, meta.SchemaVersion);
        // Stream must NOT be disposed.
        Assert.True(stream.CanRead);
    }

    // T1-004: Stream overload reads only up to $meta closing brace.
    [Fact]
    public void Peek_StreamInput_StopsAfterMetaClose()
    {
        // Build a document where $meta is followed by a large body.
        // After Peek, the stream position should be near the start (before the body).
        var sb = new StringBuilder();
        sb.Append("{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1},");
        sb.Append("\"largeBody\":\"");
        sb.Append('x', 100_000);
        sb.Append("\"}");

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        using var ms = new MemoryStream(bytes, writable: false);

        var meta = JsonEnvelope.Peek(ms);

        Assert.Equal("Test.Doc", meta.DocType);
        // After a seekable stream peek, position should be well before the end.
        // The $meta section is roughly 50 bytes; the body is 100 000+.
        // We allow up to 10 000 bytes to account for buffering headroom.
        Assert.True(ms.Position < 10_000,
            $"Expected stream position < 10000 after Peek but was {ms.Position}.");
    }

    // T1-005: A document without $meta field throws with a clear message.
    [Fact]
    public void Peek_MissingMeta_ThrowsMigrationException()
    {
        byte[] bytes = TestFixtureLoader.LoadBytes("Envelopes/missing_meta.json");
        Assert.Throws<MigrationException>(() => JsonEnvelope.Peek(bytes.AsSpan()));
    }

    // T1-006: $meta is not an object (e.g., a string or array) throws.
    [Fact]
    public void Peek_MalformedMeta_ThrowsMigrationException()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"$meta\":\"not-an-object\",\"x\":1}");
        Assert.Throws<MigrationException>(() => JsonEnvelope.Peek(bytes.AsSpan()));
    }

    // T1-007: $meta containing a field beyond the five allowed throws.
    [Fact]
    public void Peek_ExtraField_ThrowsMigrationException()
    {
        byte[] bytes = TestFixtureLoader.LoadBytes("Envelopes/extra_field_in_meta.json");
        Assert.Throws<MigrationException>(() => JsonEnvelope.Peek(bytes.AsSpan()));
    }

    // T1-008: $meta.docType is empty string throws.
    [Fact]
    public void Peek_EmptyDocType_ThrowsMigrationException()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"$meta\":{\"docType\":\"\",\"schemaVersion\":1}}");
        Assert.Throws<MigrationException>(() => JsonEnvelope.Peek(bytes.AsSpan()));
    }

    // T1-009: $meta.schemaVersion is 0 or negative throws.
    [Fact]
    public void Peek_NegativeSchemaVersion_ThrowsMigrationException()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":0}}");
        Assert.Throws<MigrationException>(() => JsonEnvelope.Peek(bytes.AsSpan()));
    }

    // T1-010: $meta.schemaVersion is a string or float throws.
    [Fact]
    public void Peek_NonIntegerSchemaVersion_ThrowsMigrationException()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":\"one\"}}");
        // The reader will fail to parse "one" as int, so this should throw MigrationException.
        Assert.Throws<MigrationException>(() => JsonEnvelope.Peek(bytes.AsSpan()));
    }

    // T1-011: Envelope at non-first position still parses; FdpLog emits a warning.
    [Fact]
    public void Peek_MetaNotFirstProperty_LogsWarningAndSucceeds()
    {
        byte[] bytes = TestFixtureLoader.LoadBytes("Envelopes/meta_not_first.json");
        // Should not throw — just log and return meta.
        var meta = JsonEnvelope.Peek(bytes.AsSpan());
        Assert.Equal("Test.Doc", meta.DocType);
        Assert.Equal(1, meta.SchemaVersion);
    }

    // T1-012: Zero-byte input throws.
    [Fact]
    public void Peek_EmptyStream_Throws()
    {
        byte[] empty = Array.Empty<byte>();
        Assert.ThrowsAny<Exception>(() => JsonEnvelope.Peek(empty.AsSpan()));
    }

    // T1-013: Binary garbage or plain text throws.
    [Fact]
    public void Peek_NonJsonContent_Throws()
    {
        byte[] garbage = Encoding.UTF8.GetBytes("this is not json at all!!!");
        Assert.ThrowsAny<Exception>(() => JsonEnvelope.Peek(garbage.AsSpan()));
    }

    // ---------------------------------------------------------------
    // DOM-based read
    // ---------------------------------------------------------------

    // T1-014: Reading from a JsonObject root works identically to peek.
    [Fact]
    public void Read_ParsedDom_ReturnsMeta()
    {
        string json = TestFixtureLoader.Load("Envelopes/valid_full.json");
        var root = JsonNode.Parse(json)!.AsObject();
        var meta = JsonEnvelope.Read(root);

        Assert.Equal("Test.Doc", meta.DocType);
        Assert.Equal(2, meta.SchemaVersion);
        Assert.Equal("1.0.0", meta.EngineVersion);
    }

    // ---------------------------------------------------------------
    // Write
    // ---------------------------------------------------------------

    // T1-015: New envelope appears at root[0].
    [Fact]
    public void Write_DomWithoutMeta_AddsMetaAsFirstProperty()
    {
        var root = new JsonObject { ["data"] = 42 };
        var meta = new DocumentMeta("Test.Doc", 1);

        JsonEnvelope.Write(root, meta);

        // First property must be $meta.
        string first = root.First().Key;
        Assert.Equal(JsonEnvelope.MetaFieldName, first);
    }

    // T1-016: Existing envelope is overwritten cleanly.
    [Fact]
    public void Write_DomWithExistingMeta_ReplacesMeta()
    {
        string json = TestFixtureLoader.Load("Envelopes/valid_full.json");
        var root = JsonNode.Parse(json)!.AsObject();

        var updated = new DocumentMeta("Test.Doc", 3, engineVersion: "2.0.0");
        JsonEnvelope.Write(root, updated);

        var readBack = JsonEnvelope.Read(root);
        Assert.Equal(3, readBack.SchemaVersion);
        Assert.Equal("2.0.0", readBack.EngineVersion);

        // $meta must still be first.
        Assert.Equal(JsonEnvelope.MetaFieldName, root.First().Key);
    }

    // ---------------------------------------------------------------
    // HasEnvelope
    // ---------------------------------------------------------------

    // T1-017: Detects valid envelope.
    [Fact]
    public void HasEnvelope_PresentValidShape_ReturnsTrue()
    {
        string json = TestFixtureLoader.Load("Envelopes/valid_basic.json");
        var root = JsonNode.Parse(json)!.AsObject();
        Assert.True(JsonEnvelope.HasEnvelope(root));
    }

    // T1-018: Returns false without throwing.
    [Fact]
    public void HasEnvelope_AbsentOrMalformed_ReturnsFalse()
    {
        string json = TestFixtureLoader.Load("Envelopes/missing_meta.json");
        var root = JsonNode.Parse(json)!.AsObject();
        Assert.False(JsonEnvelope.HasEnvelope(root));

        // $meta is a string, not an object.
        var malformed = new JsonObject { ["$meta"] = "not-an-object" };
        Assert.False(JsonEnvelope.HasEnvelope(malformed));
    }

    // ---------------------------------------------------------------
    // Update helpers
    // ---------------------------------------------------------------

    // T1-019: Updates SchemaVersion, leaves other fields unchanged.
    [Fact]
    public void WithSchemaVersion_PreservesOtherFields()
    {
        var meta = new DocumentMeta("Test.Doc", 1, "1.0", "Tool",
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var updated = JsonEnvelope.WithSchemaVersion(meta, 5);

        Assert.Equal(5, updated.SchemaVersion);
        Assert.Equal("Test.Doc", updated.DocType);
        Assert.Equal("1.0", updated.EngineVersion);
        Assert.Equal("Tool", updated.CreatedBy);
        Assert.Equal(meta.CreatedUtc, updated.CreatedUtc);
    }

    // T1-020: Updates EngineVersion only.
    [Fact]
    public void WithEngineVersion_PreservesOtherFields()
    {
        var meta = new DocumentMeta("Test.Doc", 2, "1.0");
        var updated = JsonEnvelope.WithEngineVersion(meta, "2.5.3");

        Assert.Equal("2.5.3", updated.EngineVersion);
        Assert.Equal("Test.Doc", updated.DocType);
        Assert.Equal(2, updated.SchemaVersion);
    }

    // ---------------------------------------------------------------
    // Additional coverage tests (T1-021..T1-024)
    // ---------------------------------------------------------------

    // T1-021: Read from DOM with no optional fields returns null for optional props.
    [Fact]
    public void Read_DomMeta_WithNullOptionalFields_Succeeds()
    {
        var root = JsonNode.Parse(
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1}}")!.AsObject();

        var meta = JsonEnvelope.Read(root);

        Assert.Equal("Test.Doc", meta.DocType);
        Assert.Equal(1, meta.SchemaVersion);
        Assert.Null(meta.EngineVersion);
        Assert.Null(meta.CreatedBy);
        Assert.Null(meta.CreatedUtc);
    }

    // T1-022: Read succeeds even when $meta is not the first property (logs warning).
    [Fact]
    public void Read_DomMeta_MetaNotFirstProperty_LogsWarningButSucceeds()
    {
        var root = JsonNode.Parse(
            "{\"other\":99,\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1}}")!.AsObject();

        var meta = JsonEnvelope.Read(root);

        Assert.Equal("Test.Doc", meta.DocType);
        Assert.Equal(1, meta.SchemaVersion);
    }

    // T1-023: Peek with non-seekable stream buffers and returns correct meta.
    [Fact]
    public void Peek_StreamNonSeekable_Works()
    {
        var json = "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":3}}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var ms = new System.IO.MemoryStream(bytes);
        using var nonSeekable = new NonSeekableStreamWrapper(ms);

        var meta = JsonEnvelope.Peek(nonSeekable);

        Assert.Equal("Test.Doc", meta.DocType);
        Assert.Equal(3, meta.SchemaVersion);
    }

    // T1-024: Read from DOM where $meta.docType is null throws MigrationException.
    [Fact]
    public void Read_DomMeta_NullDocType_ThrowsMigrationException()
    {
        var root = JsonNode.Parse(
            "{\"$meta\":{\"docType\":null,\"schemaVersion\":1}}")!.AsObject();

        Assert.Throws<MigrationException>(() => JsonEnvelope.Read(root));
    }

    // T1-025: Read from DOM with no $meta property throws MigrationException.
    [Fact]
    public void Read_Dom_MissingMeta_ThrowsMigrationException()
    {
        var root = JsonNode.Parse("{\"other\":1,\"data\":2}")!.AsObject();

        Assert.Throws<MigrationException>(() => JsonEnvelope.Read(root));
    }

    // T1-026: Read from DOM with $meta as a string (not object) throws MigrationException.
    [Fact]
    public void Read_Dom_MetaIsNotObject_ThrowsMigrationException()
    {
        var root = JsonNode.Parse("{\"$meta\":\"should-be-object\"}")!.AsObject();

        Assert.Throws<MigrationException>(() => JsonEnvelope.Read(root));
    }

    // T1-027: Write with optional fields (EngineVersion, CreatedBy, CreatedUtc) sets them.
    [Fact]
    public void Write_WithAllOptionalFields_WritesOptionalFieldsToMeta()
    {
        var utcNow = new System.DateTime(2024, 1, 15, 12, 0, 0, System.DateTimeKind.Utc);
        var meta = new DocumentMeta("Test.Doc", 1, "engine-2.0", "Alice", utcNow);
        var root = JsonNode.Parse("{\"data\":99}")!.AsObject();

        JsonEnvelope.Write(root, meta);

        var metaObj = root["$meta"]!.AsObject();
        Assert.Equal("engine-2.0", metaObj["engineVersion"]!.GetValue<string>());
        Assert.Equal("Alice", metaObj["createdBy"]!.GetValue<string>());
        Assert.NotNull(metaObj["createdUtc"]);
    }

    // T1-028: Peek on a stream starting with '[' (not object) throws MigrationException.
    [Fact]
    public void Peek_NonObjectRoot_ThrowsMigrationException()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("[1,2,3]");

        Assert.Throws<MigrationException>(() =>
            JsonEnvelope.Peek(new System.ReadOnlySpan<byte>(bytes)));
    }

    // T1-029: Peek on '{}' (object with no $meta) throws MigrationException.
    [Fact]
    public void Peek_EmptyObject_ThrowsMigrationException()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("{}");

        Assert.Throws<MigrationException>(() =>
            JsonEnvelope.Peek(new System.ReadOnlySpan<byte>(bytes)));
    }

    // T1-030: Peek on stream where $meta.docType is null throws MigrationException.
    [Fact]
    public void Peek_Stream_NullDocType_ThrowsMigrationException()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            "{\"$meta\":{\"docType\":null,\"schemaVersion\":1}}");

        Assert.Throws<MigrationException>(() =>
            JsonEnvelope.Peek(new System.ReadOnlySpan<byte>(bytes)));
    }

    // T1-031: Peek on stream where $meta.docType is "" throws MigrationException.
    [Fact]
    public void Peek_Stream_EmptyDocType_ThrowsMigrationException()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            "{\"$meta\":{\"docType\":\"\",\"schemaVersion\":1}}");

        Assert.Throws<MigrationException>(() =>
            JsonEnvelope.Peek(new System.ReadOnlySpan<byte>(bytes)));
    }

    // T1-032: Peek on stream where $meta has no schemaVersion throws MigrationException.
    [Fact]
    public void Peek_Stream_MissingSchemaVersion_ThrowsMigrationException()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            "{\"$meta\":{\"docType\":\"Test.Doc\"}}");

        Assert.Throws<MigrationException>(() =>
            JsonEnvelope.Peek(new System.ReadOnlySpan<byte>(bytes)));
    }

    // T1-033: Peek on stream where $meta has an unrecognized field throws MigrationException.
    [Fact]
    public void Peek_Stream_UnrecognizedMetaField_ThrowsMigrationException()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1,\"badField\":\"x\"}}");

        Assert.Throws<MigrationException>(() =>
            JsonEnvelope.Peek(new System.ReadOnlySpan<byte>(bytes)));
    }

    // T1-034: Read from DOM where $meta.docType is "" throws MigrationException.
    [Fact]
    public void Read_Dom_EmptyDocType_ThrowsMigrationException()
    {
        var root = JsonNode.Parse(
            "{\"$meta\":{\"docType\":\"\",\"schemaVersion\":1}}")!.AsObject();

        Assert.Throws<MigrationException>(() => JsonEnvelope.Read(root));
    }

    // T1-035: Read from DOM where $meta has no schemaVersion throws MigrationException.
    [Fact]
    public void Read_Dom_MissingSchemaVersion_ThrowsMigrationException()
    {
        var root = JsonNode.Parse(
            "{\"$meta\":{\"docType\":\"Test.Doc\"}}")!.AsObject();

        Assert.Throws<MigrationException>(() => JsonEnvelope.Read(root));
    }

    // T1-036: Read from DOM where $meta has an unrecognized field throws MigrationException.
    [Fact]
    public void Read_Dom_UnrecognizedMetaField_ThrowsMigrationException()
    {
        var root = JsonNode.Parse(
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1,\"unknown\":\"x\"}}")!.AsObject();

        Assert.Throws<MigrationException>(() => JsonEnvelope.Read(root));
    }

    // T1-037: Peek skips an object-valued property before $meta (exercises SkipValue/StartObject).
    [Fact]
    public void Peek_ObjectValueBeforeMeta_SkipsAndReturnsEnvelope()
    {
        var json =
            "{\"nested\":{\"a\":1,\"b\":{\"deep\":2}}," +
            "\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":3}}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        var meta = JsonEnvelope.Peek(new System.ReadOnlySpan<byte>(bytes));

        Assert.Equal("Test.Doc", meta.DocType);
        Assert.Equal(3, meta.SchemaVersion);
    }

    // T1-038: Peek skips an array-valued property before $meta (exercises SkipValue/StartArray).
    [Fact]
    public void Peek_ArrayValueBeforeMeta_SkipsAndReturnsEnvelope()
    {
        var json =
            "{\"items\":[1,[2,3],{\"x\":4}]," +
            "\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":5}}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        var meta = JsonEnvelope.Peek(new System.ReadOnlySpan<byte>(bytes));

        Assert.Equal("Test.Doc", meta.DocType);
        Assert.Equal(5, meta.SchemaVersion);
    }

    // ---------------------------------------------------------------
    // Non-seekable stream helper used by T1-023
    // ---------------------------------------------------------------

    private sealed class NonSeekableStreamWrapper(System.IO.Stream inner) : System.IO.Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
            => inner.Read(buffer, offset, count);
        public override long Seek(long offset, System.IO.SeekOrigin origin)
            => throw new NotSupportedException();
        public override void SetLength(long value)
            => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }
}
