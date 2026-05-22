using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Catalog of all node kinds known to the host. Used by the search popup
/// and picker to populate "Add Node" lists.
/// </summary>
public interface INodeCatalog
{
    /// <summary>All registered node kinds.</summary>
    IReadOnlyList<NodeCatalogEntry> All { get; }

    /// <summary>Top-level categories used for grouping.</summary>
    IReadOnlyList<NodeCategoryDescriptor> Categories { get; }

    /// <summary>Search by free text and optional filters.</summary>
    IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q);

    /// <summary>
    /// Search filtered by pin context: source pin's direction and type,
    /// to support "drag wire onto empty canvas → only compatible nodes".
    /// </summary>
    IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q);
}

/// <summary>One entry in the catalog (corresponds to a node kind).</summary>
public sealed record NodeCatalogEntry(
    NodeKindKey Kind,
    string DisplayName,
    string? Description,
    string? CategoryPath,
    IReadOnlyList<string> Keywords,
    string? IconKey,
    bool IsPure,
    bool IsLatent,
    bool IsDeprecated,
    IReadOnlyList<PinSignature> Inputs,
    IReadOnlyList<PinSignature> Outputs);

/// <summary>Signature of a single pin used at catalog lookup time.</summary>
public sealed record PinSignature(
    string Label,
    PinKind Kind,
    TypeKey? Type,
    bool IsWildcard);

/// <summary>Descriptor for a top-level catalog category.</summary>
public sealed record NodeCategoryDescriptor(
    string Path,
    string DisplayName,
    string? IconKey);

/// <summary>Search query for the catalog.</summary>
public sealed record NodeSearchQuery(
    string Text,
    string? CategoryFilter = null,
    TypeKey? TypeFilter = null,
    bool IncludeDeprecated = false);

/// <summary>Query for "what can connect to this pin?"</summary>
public sealed record PinContextQuery(
    PinId SourcePin,
    PinDirection SourceDirection,
    PinKind SourceKind,
    TypeKey? SourceType,
    string Text);
