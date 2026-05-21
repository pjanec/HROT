namespace Hrot.Blueprints.Core.Compiler.Emit;

/// <summary>
/// Maps generated C# source line numbers back to source Blueprint node IDs.
/// </summary>
public sealed record DebugMap
{
    public Guid  AssetId       { get; init; }
    public int   BlueprintId   { get; init; }
    public ulong StructureHash { get; init; }
    public IReadOnlyList<DebugMapEntry> Entries { get; init; } = Array.Empty<DebugMapEntry>();
}

public sealed record DebugMapEntry(Guid NodeId, Guid GraphId, int StartLine, int EndLine);

/// <summary>
/// Tracks source spans alongside emission.
/// </summary>
internal sealed class DebugMapBuilder
{
    private readonly List<DebugMapEntry> _entries = new();
    private readonly Dictionary<Guid, (Guid GraphId, int StartLine)> _openNodes = new();
    private readonly Guid  _assetId;
    private readonly int   _blueprintId;
    private readonly ulong _structureHash;

    public DebugMapBuilder() { }

    public DebugMapBuilder(Guid assetId) { _assetId = assetId; }

    public DebugMapBuilder(Guid assetId, int blueprintId, ulong structureHash)
    {
        _assetId       = assetId;
        _blueprintId   = blueprintId;
        _structureHash = structureHash;
    }

    public void Record(Guid nodeId, Guid graphId, int startLine, int endLine)
        => _entries.Add(new DebugMapEntry(nodeId, graphId, startLine, endLine));

    public void RecordNodeStart(Guid nodeId, Guid graphId, int line)
        => _openNodes.TryAdd(nodeId, (graphId, line));

    public void RecordNodeEnd(Guid nodeId, int line)
    {
        if (!_openNodes.Remove(nodeId, out var info)) return;
        Record(nodeId, info.GraphId, info.StartLine, line);
    }

    public DebugMap Build() => new DebugMap
    {
        AssetId       = _assetId,
        BlueprintId   = _blueprintId,
        StructureHash = _structureHash,
        Entries       = _entries.AsReadOnly(),
    };
}
