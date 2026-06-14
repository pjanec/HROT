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

/// <summary>
/// Determines how the node picker handles picking this catalog entry.
/// </summary>
public enum NodePaletteAction
{
    /// <summary>Default: emit <c>AddNode</c> as a free node (existing behaviour).</summary>
    CreateNode,
    /// <summary>
    /// Emit <c>AddAttachment</c> on the single currently-selected node.
    /// If zero or more than one node is selected the pick is silently ignored.
    /// </summary>
    AttachToSelected,
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
    IReadOnlyList<PinSignature> Outputs,
    NodePaletteAction PaletteAction = NodePaletteAction.CreateNode,
    AttachmentCategory? AttachmentCategory = null);

/// <summary>
/// Well-known keys used in <c>HostProperties</c> dictionaries passed to
/// <c>GraphCommand.AddAttachment</c> by the node picker.
/// Defined here so both NodeEditor core and all host projects can reference
/// them without a dependency on any specific host assembly.
/// </summary>
public static class AttachmentHostPropertyKeys
{
    /// <summary>
    /// The <c>NodeKindKey.Value</c> string of the catalog entry that
    /// triggered the attachment creation.  The host sink uses this to decide
    /// which concrete attachment type to create.
    /// </summary>
    public const string Kind = "paletteKind";
}

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
