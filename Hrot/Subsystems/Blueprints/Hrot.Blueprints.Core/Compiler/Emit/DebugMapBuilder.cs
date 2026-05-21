namespace Hrot.Blueprints.Core.Compiler.Emit;

/// <summary>
/// Maps generated C# source line numbers back to source Blueprint node IDs.
/// </summary>
public sealed record DebugMap
{
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

    public DebugMapBuilder() { }

    public DebugMapBuilder(Guid assetId) { _ = assetId; }

    public void Record(Guid nodeId, Guid graphId, int startLine, int endLine)
        => _entries.Add(new DebugMapEntry(nodeId, graphId, startLine, endLine));

    public void RecordNodeStart(Guid nodeId, Guid graphId, int line)
        => _openNodes.TryAdd(nodeId, (graphId, line));

    public void RecordNodeEnd(Guid nodeId, int line)
    {
        if (!_openNodes.Remove(nodeId, out var info)) return;
        Record(nodeId, info.GraphId, info.StartLine, line);
    }

    public DebugMap Build() => new DebugMap { Entries = _entries.AsReadOnly() };
}
