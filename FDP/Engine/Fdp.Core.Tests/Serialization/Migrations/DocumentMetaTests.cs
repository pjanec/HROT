using Fdp.Core.Serialization.Migrations;

namespace Fdp.Core.Tests.Serialization.Migrations;

public class DocumentMetaTests
{
    // T1-030: Standard happy path.
    [Fact]
    public void Constructor_ValidInputs_Succeeds()
    {
        var meta = new DocumentMeta("Test.Doc", 1);

        Assert.Equal("Test.Doc", meta.DocType);
        Assert.Equal(1, meta.SchemaVersion);
        Assert.Null(meta.EngineVersion);
        Assert.Null(meta.CreatedBy);
        Assert.Null(meta.CreatedUtc);
    }

    // T1-031: DocType validation.
    [Fact]
    public void Constructor_EmptyDocType_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new DocumentMeta("", 1));
    }

    // T1-032: Null guard.
    [Fact]
    public void Constructor_NullDocType_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new DocumentMeta(null!, 1));
    }

    // T1-033: Version >= 1 enforced (zero).
    [Fact]
    public void Constructor_ZeroSchemaVersion_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentMeta("Test.Doc", 0));
    }

    // T1-034: Version validation (negative).
    [Fact]
    public void Constructor_NegativeSchemaVersion_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentMeta("Test.Doc", -5));
    }

    // T1-035: Local/Unspecified DateTime is interpreted as UTC; warning logged.
    [Fact]
    public void Constructor_NonUtcCreatedUtc_CoercesAndLogsWarning()
    {
        var localDt = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Local);
        var meta = new DocumentMeta("Test.Doc", 1, createdUtc: localDt);

        // The value must be stored with UTC Kind regardless of the input Kind.
        Assert.NotNull(meta.CreatedUtc);
        Assert.Equal(DateTimeKind.Utc, meta.CreatedUtc!.Value.Kind);
        Assert.Equal(localDt.Year, meta.CreatedUtc.Value.Year);
        Assert.Equal(localDt.Month, meta.CreatedUtc.Value.Month);
        Assert.Equal(localDt.Day, meta.CreatedUtc.Value.Day);
        Assert.Equal(localDt.Hour, meta.CreatedUtc.Value.Hour);
    }
}
