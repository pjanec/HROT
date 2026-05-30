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
    private readonly Dictionary<string, NodeMapEntry>  _nodesByString;
    private readonly Dictionary<Guid, NodeMapEntry>    _nodesByGuid;
    private readonly Dictionary<Guid, DebugPinInfo>    _pinsByGuid;
    private readonly Dictionary<Guid, DebugGraphInfo>  _graphsByGuid;

    public Guid             AssetId             { get; }
    public string           AssetName           { get; }
    public int              BlueprintId         { get; }
    public ulong            StructureHash       { get; }
    public string           GeneratedSourcePath { get; }
    public DebugStateLayout StateLayout         { get; }

    public DebugMapIndex(DebugMap map)
    {
        AssetId             = map.AssetId;
        AssetName           = !string.IsNullOrEmpty(map.AssetName)
                                  ? map.AssetName
                                  : map.AssetId.ToString("D");
        BlueprintId         = map.BlueprintId;
        StructureHash       = map.StructureHash;
        GeneratedSourcePath = map.GeneratedSourcePath;
        StateLayout         = map.StateLayout;

        _nodesByString = new Dictionary<string, NodeMapEntry>(StringComparer.Ordinal);
        _nodesByGuid   = new Dictionary<Guid, NodeMapEntry>();
        _pinsByGuid    = new Dictionary<Guid, DebugPinInfo>();
        _graphsByGuid  = new Dictionary<Guid, DebugGraphInfo>();

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

        foreach (var graph in map.Graphs)
            _graphsByGuid[graph.GraphId] = graph;

        foreach (var pin in map.Pins)
            _pinsByGuid[pin.PinId] = pin;
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

    /// <summary>Resolve a pin by Guid. Returns null if not found.</summary>
    public DebugPinInfo? TryGetPinById(Guid pinId)
        => _pinsByGuid.TryGetValue(pinId, out var p) ? p : null;

    /// <summary>Resolve a graph by Guid. Returns null if not found.</summary>
    public DebugGraphInfo? TryGetGraphById(Guid graphId)
        => _graphsByGuid.TryGetValue(graphId, out var g) ? g : null;

    /// <summary>All indexed nodes in this map.</summary>
    public IReadOnlyCollection<NodeMapEntry> AllNodes  => _nodesByGuid.Values;

    /// <summary>All indexed pins in this map.</summary>
    public IReadOnlyCollection<DebugPinInfo> AllPins   => _pinsByGuid.Values;

    /// <summary>All indexed graphs in this map.</summary>
    public IReadOnlyCollection<DebugGraphInfo> AllGraphs => _graphsByGuid.Values;
}
