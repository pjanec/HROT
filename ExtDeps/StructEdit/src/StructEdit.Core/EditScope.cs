namespace StructEdit.Core;

/// <summary>
/// Controls which fields appear in the EditDocument.
/// The edit buffer always stores the whole component regardless.
/// </summary>
public sealed class EditScope
{
    private static readonly EditScope _wholeComponent = new()
    {
        IncludedPaths = Array.Empty<EditPath>(),
        IncludeChildren = true,
        IncludeParentsForContext = false,
    };

    /// <summary>
    /// Singleton scope meaning "all fields" — no path filtering applied.
    /// </summary>
    public static EditScope WholeComponent => _wholeComponent;

    /// <summary>Paths included in the scope. Empty means all fields.</summary>
    public required IReadOnlyList<EditPath> IncludedPaths { get; init; }

    /// <summary>When true, children of included paths are also included. Default: true.</summary>
    public bool IncludeChildren { get; init; } = true;

    /// <summary>
    /// When true, ancestor nodes of included paths appear in the document as read-only
    /// context nodes (so the renderer can show the full path hierarchy).
    /// Default: false.
    /// </summary>
    public bool IncludeParentsForContext { get; init; } = false;

    /// <summary>Creates a scope targeting a single field path.</summary>
    public static EditScope ForField(EditPath path) => new()
    {
        IncludedPaths = new[] { path },
        IncludeChildren = true,
        IncludeParentsForContext = false,
    };

    /// <summary>Creates a scope targeting multiple field paths.</summary>
    public static EditScope ForFields(params EditPath[] paths) => new()
    {
        IncludedPaths = paths,
        IncludeChildren = true,
        IncludeParentsForContext = false,
    };
}
