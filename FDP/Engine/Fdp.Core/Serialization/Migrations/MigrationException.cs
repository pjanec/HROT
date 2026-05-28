namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Thrown for unrecoverable migration failures. Extends
/// <see cref="InvalidOperationException"/> for compatibility with the
/// engine's existing fail-loud exception pattern in cluster load handlers
/// and the editor's global alert modal.
/// </summary>
public class MigrationException : InvalidOperationException
{
    /// <summary>The document type involved, if known.</summary>
    public string? DocType { get; }

    /// <summary>The source schema version, if known.</summary>
    public int? FromVersion { get; }

    /// <summary>The target schema version, if known.</summary>
    public int? ToVersion { get; }

    /// <summary>The source file path, if the migration was file-backed.</summary>
    public string? SourcePath { get; }

    /// <summary>
    /// The JSONPath where the failure occurred, if a scope was active
    /// when the exception was raised.
    /// </summary>
    public string? Path { get; }

    /// <summary>Creates a migration exception with a message only.</summary>
    public MigrationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a migration exception with a message and an inner exception.</summary>
    public MigrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates a migration exception with full migration context fields.</summary>
    public MigrationException(
        string message,
        string? docType,
        int? fromVersion,
        int? toVersion,
        string? sourcePath,
        string? path,
        Exception? innerException = null)
        : base(message, innerException)
    {
        DocType = docType;
        FromVersion = fromVersion;
        ToVersion = toVersion;
        SourcePath = sourcePath;
        Path = path;
    }
}
