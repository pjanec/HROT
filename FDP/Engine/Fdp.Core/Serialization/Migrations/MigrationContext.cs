using Fdp.Core.Serialization.Migrations.Internal;

namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Carries shared state for a single migration run. Passed to every
/// <see cref="IJsonDocumentMigrator.Apply"/> call in the chain.
/// <para>
/// Migrators use this type to push/pop JSONPath scopes so that
/// <see cref="MigrationWarning.Path"/> values in <see cref="Report"/>
/// point at the exact field that triggered the warning.
/// </para>
/// </summary>
/// <remarks>
/// Constructor is <see langword="internal"/> — only the migration pipeline
/// may create contexts.
/// </remarks>
public sealed class MigrationContext
{
    private readonly ScopePathStack _stack = new();

    /// <summary>
    /// Creates a new context with a simplified signature used by unit tests
    /// and any caller that does not yet know version information.
    /// </summary>
    internal MigrationContext(string docType, string? sourcePath)
        : this(docType, 0, 0, MigrationDirection.Up, sourcePath)
    {
    }

    /// <summary>
    /// Creates a new context with full migration metadata. Only the migration
    /// pipeline should call this overload.
    /// </summary>
    internal MigrationContext(string docType, int fromVersion, int toVersion,
        MigrationDirection direction, string? sourcePath)
    {
        SourcePath = sourcePath;
        Report = new MigrationReport(docType, fromVersion, toVersion, direction);
    }

    /// <summary>
    /// The file that was loaded, or <c>null</c> when migrating an
    /// in-memory document.
    /// </summary>
    public string? SourcePath { get; }

    /// <summary>
    /// Accumulated report for this migration run. Migrators may call
    /// <see cref="AddNote"/> and <see cref="AddWarning"/> rather than
    /// accessing the report directly.
    /// </summary>
    public MigrationReport Report { get; }

    /// <summary>
    /// The current JSONPath reflecting all active <see cref="WithItem"/>,
    /// <see cref="WithIndex"/>, and <see cref="WithPathSuffix"/> scopes.
    /// Returns <c>"$"</c> when no scope is active.
    /// </summary>
    public string CurrentPath => _stack.CurrentPath;

    // ---------------------------------------------------------------
    // Scope helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Pushes a named-property segment on to the path stack.
    /// The segment is canonicalized: dotted form for plain identifiers,
    /// bracketed form for keys containing special characters.
    /// Disposes the returned token to pop.
    /// </summary>
    public IDisposable WithItem(string key) => _stack.PushItem(key);

    /// <summary>
    /// Pushes an array-index segment on to the path stack.
    /// Disposes the returned token to pop.
    /// </summary>
    public IDisposable WithIndex(int index) => _stack.PushIndex(index);

    /// <summary>
    /// Pushes a pre-built multi-segment suffix on to the path stack.
    /// Use when the caller has already canonicalized the sub-path.
    /// Disposes the returned token to pop.
    /// </summary>
    public IDisposable WithPathSuffix(string suffix) => _stack.PushSuffix(suffix);

    // ---------------------------------------------------------------
    // Warning / note helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Raises a non-fatal warning. The <see cref="CurrentPath"/> at the
    /// time of the call is captured into <see cref="MigrationWarning.Path"/>
    /// automatically.
    /// </summary>
    public void AddWarning(string message)
    {
        var warning = new MigrationWarning(message, CurrentPath);
        Report.AddWarning(warning);
    }

    /// <summary>Adds a free-form note to <see cref="Report"/>.</summary>
    public void AddNote(string note) => Report.AddNote(note);
}
