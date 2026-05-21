namespace Hrot.Blueprints.Core.Compiler.Emit;

/// <summary>
/// Maps generated C# source line numbers back to source Blueprint node IDs.
/// Full implementation in TASK-CP-004.
/// </summary>
public sealed record DebugMap
{
    public IReadOnlyList<DebugMapEntry> Entries { get; init; } = Array.Empty<DebugMapEntry>();
}

public sealed record DebugMapEntry(Guid NodeId, Guid GraphId, int StartLine, int EndLine);

/// <summary>
/// Tracks source spans alongside emission. Full implementation in TASK-CP-004.
/// </summary>
internal sealed class DebugMapBuilder
{
    private readonly List<DebugMapEntry> _entries = new();

    public void Record(Guid nodeId, Guid graphId, int startLine, int endLine)
        => _entries.Add(new DebugMapEntry(nodeId, graphId, startLine, endLine));

    public DebugMap Build() => new DebugMap { Entries = _entries.AsReadOnly() };
}
