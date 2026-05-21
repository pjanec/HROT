using Hrot.Blueprints.Core.Compiler.Emit;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Immutable runtime index for a single asset's debug map.
/// Provides O(1) lookup by string node-id (hot probe path) and by Guid (editor UI).
/// Created by BlueprintDebugSession.RegisterDebugMap; keyed by AssetId.
/// </summary>
public sealed record NodeMapEntry(
    Guid   NodeId,
    string NodeIdString,
    Guid   GraphId,
    string NodeKind,
    string DisplayName,
    int    SourceStartLine,
    int    SourceEndLine,
    int?   PhaseIndex);

public sealed class DebugMapIndex
{
    private readonly Dictionary<string, NodeMapEntry> _nodesByString;
    private readonly Dictionary<Guid, NodeMapEntry>   _nodesByGuid;

    public Guid   AssetId       { get; }
    // AssetName: DebugMap does not carry a name field; use AssetId string as fallback.
    public string AssetName     { get; }
    public ulong  StructureHash { get; }

    public DebugMapIndex(DebugMap map)
    {
        AssetId       = map.AssetId;
        AssetName     = map.AssetId.ToString("D");
        StructureHash = map.StructureHash;

        _nodesByString = new Dictionary<string, NodeMapEntry>(StringComparer.Ordinal);
        _nodesByGuid   = new Dictionary<Guid, NodeMapEntry>();

        foreach (var entry in map.Entries)
        {
            var nodeEntry = new NodeMapEntry(
                NodeId:          entry.NodeId,
                NodeIdString:    entry.NodeId.ToString("D"),
                GraphId:         entry.GraphId,
                NodeKind:        entry.NodeKind,
                DisplayName:     entry.DisplayName,
                SourceStartLine: entry.StartLine,
                SourceEndLine:   entry.EndLine,
                PhaseIndex:      entry.PhaseIndex);
            _nodesByString[nodeEntry.NodeIdString] = nodeEntry;
            _nodesByGuid[entry.NodeId]             = nodeEntry;
        }
    }

    /// <summary>
    /// Resolve a node by the string node-id emitted by DebugProbe (lowercase hyphenated Guid, "D" format).
    /// Returns null if not found.
    /// </summary>
    public NodeMapEntry? TryResolveNode(string nodeIdString)
        => _nodesByString.TryGetValue(nodeIdString, out var e) ? e : null;

    /// <summary>
    /// Resolve a node by Guid. Returns null if not found.
    /// </summary>
    public NodeMapEntry? TryResolveNode(Guid nodeId)
        => _nodesByGuid.TryGetValue(nodeId, out var e) ? e : null;

    /// <summary>All indexed nodes in this map.</summary>
    public IReadOnlyCollection<NodeMapEntry> AllNodes => _nodesByGuid.Values;
}
