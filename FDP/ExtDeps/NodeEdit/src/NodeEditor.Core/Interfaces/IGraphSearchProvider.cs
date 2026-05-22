namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Extension point for the Find-in-Graph feature. Host implements this to
/// expose extra searchable text per node (e.g., bound variable references).
/// </summary>
public interface IGraphSearchProvider
{
    /// <summary>Return whitespace-separated searchable terms for a node.</summary>
    string GetSearchableText(INodeModel node);
}
