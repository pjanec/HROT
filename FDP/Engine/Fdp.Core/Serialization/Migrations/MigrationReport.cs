namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Structured summary of what a single migration run accomplished.
/// Built up by migrators via <see cref="MigrationContext.Report"/> and
/// returned to callers.
/// </summary>
public sealed class MigrationReport
{
    private readonly List<string> _notes = new();
    private readonly List<MigrationWarning> _warnings = new();

    /// <summary>Constructor is internal — only the pipeline creates reports.</summary>
    internal MigrationReport(string docType, int fromVersion, int toVersion, MigrationDirection direction)
    {
        DocType = docType;
        FromVersion = fromVersion;
        ToVersion = toVersion;
        Direction = direction;
    }

    /// <summary>The document type that was migrated.</summary>
    public string DocType { get; }

    /// <summary>The schema version before migration.</summary>
    public int FromVersion { get; }

    /// <summary>The schema version after migration.</summary>
    public int ToVersion { get; }

    /// <summary>Whether migration went up or down.</summary>
    public MigrationDirection Direction { get; }

    /// <summary>Total wall-clock duration of the migration chain.</summary>
    public TimeSpan Duration { get; internal set; }

    /// <summary>
    /// Free-form human-readable notes added by migrators.
    /// </summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>
    /// Warnings raised during migration that did not prevent completion.
    /// </summary>
    public IReadOnlyList<MigrationWarning> Warnings => _warnings;

    /// <summary>Adds a note. Called by migrators.</summary>
    public void AddNote(string note) => _notes.Add(note);

    /// <summary>
    /// Adds a warning. The current JSONPath is supplied by the caller
    /// (typically <see cref="MigrationContext"/>).
    /// </summary>
    internal void AddWarning(MigrationWarning warning) => _warnings.Add(warning);

    /// <summary>
    /// Adds a warning with an explicit path. Called by migrators via
    /// <see cref="MigrationContext.AddWarning"/>.
    /// </summary>
    internal void AddWarning(string message) => _warnings.Add(new MigrationWarning(message, "$"));
}
