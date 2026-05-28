using Fdp.Core.Logging;

namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// The contents of a JSON document's <c>$meta</c> envelope. Carries the
/// document type identifier and schema version (load-bearing for migration
/// routing) plus optional diagnostic fields preserved across saves but
/// never inspected by migrators.
/// </summary>
public sealed record DocumentMeta
{
    /// <summary>
    /// The document type identifier, e.g. <c>"Hrot.Scenario"</c>.
    /// </summary>
    public string DocType { get; init; }

    /// <summary>
    /// The schema version. Always at least 1.
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Optional diagnostic field: the engine build that last wrote the file.
    /// </summary>
    public string? EngineVersion { get; init; }

    /// <summary>
    /// Optional diagnostic field: the tool that authored or last wrote the file.
    /// </summary>
    public string? CreatedBy { get; init; }

    /// <summary>
    /// Optional diagnostic field: when the file was first authored. Immutable across migrations.
    /// Always UTC when set.
    /// </summary>
    public DateTime? CreatedUtc { get; init; }

    /// <summary>
    /// Constructs a <see cref="DocumentMeta"/> with validation.
    /// </summary>
    /// <param name="docType">Non-null, non-empty document type identifier.</param>
    /// <param name="schemaVersion">Must be at least 1.</param>
    /// <param name="engineVersion">Optional engine version string.</param>
    /// <param name="createdBy">Optional authoring tool name.</param>
    /// <param name="createdUtc">Optional creation timestamp. Coerced to UTC if non-UTC.</param>
    public DocumentMeta(
        string docType,
        int schemaVersion,
        string? engineVersion = null,
        string? createdBy = null,
        DateTime? createdUtc = null)
    {
        if (docType is null)
            throw new ArgumentException("DocType must not be null.", nameof(docType));
        if (docType.Length == 0)
            throw new ArgumentException("DocType must not be empty.", nameof(docType));
        if (schemaVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "SchemaVersion must be >= 1.");

        DocType = docType;
        SchemaVersion = schemaVersion;
        EngineVersion = engineVersion;
        CreatedBy = createdBy;

        if (createdUtc.HasValue)
        {
            if (createdUtc.Value.Kind != DateTimeKind.Utc)
            {
                FdpLog<DocumentMeta>.Warn(
                    "DocumentMeta: CreatedUtc has Kind={0}; coercing to UTC.",
                    createdUtc.Value.Kind);
                CreatedUtc = DateTime.SpecifyKind(createdUtc.Value, DateTimeKind.Utc);
            }
            else
            {
                CreatedUtc = createdUtc.Value;
            }
        }
    }
}
