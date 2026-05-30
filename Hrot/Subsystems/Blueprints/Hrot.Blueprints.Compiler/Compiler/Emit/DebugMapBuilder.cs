namespace Hrot.Blueprints.Core.Compiler.Emit;

// ---- Debug map graph / pin / state-layout types ----------------------------

/// <summary>One graph entry in the debug map (id + human name + kind).</summary>
public sealed record DebugGraphInfo(Guid GraphId, string GraphName, string GraphKind);

/// <summary>One data-or-exec pin entry in the debug map.</summary>
public sealed record DebugPinInfo(
    Guid   PinId,
    Guid   NodeId,
    string PinName,
    string PinDirection,
    string PinKind,
    string TypeFullName,
    string ValueAccessExpression);

/// <summary>One field in the state-layout section of the debug map.</summary>
public sealed record StateLayoutField(string Name, string Type, int OffsetBytes, int SizeBytes);

/// <summary>State-layout section of the debug map (field-by-field memory layout).</summary>
public sealed record DebugStateLayout
{
    public IReadOnlyList<StateLayoutField> Fields { get; init; } = Array.Empty<StateLayoutField>();
}

// ---- Core debug map types --------------------------------------------------

/// <summary>
/// Maps generated C# source line numbers back to source Blueprint node IDs,
/// and carries pin/graph/state-layout metadata for the debugger.
/// </summary>
public sealed record DebugMap
{
    public Guid   AssetId             { get; init; }
    public string AssetName           { get; init; } = string.Empty;
    public int    BlueprintId         { get; init; }
    public ulong  StructureHash       { get; init; }
    public string GeneratedSourcePath { get; init; } = string.Empty;
    public IReadOnlyList<DebugMapEntry>  Entries     { get; init; } = Array.Empty<DebugMapEntry>();
    public IReadOnlyList<DebugGraphInfo> Graphs      { get; init; } = Array.Empty<DebugGraphInfo>();
    public IReadOnlyList<DebugPinInfo>   Pins        { get; init; } = Array.Empty<DebugPinInfo>();
    public DebugStateLayout StateLayout { get; init; } = new DebugStateLayout();
}

public sealed record DebugMapEntry(Guid NodeId, Guid GraphId, int StartLine, int EndLine)
{
    public string NodeKind    { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int?   PhaseIndex  { get; init; } = null;
}

/// <summary>
/// Tracks source spans alongside emission, plus graph/pin/state-layout metadata.
/// </summary>
internal sealed class DebugMapBuilder
{
    private readonly List<DebugMapEntry>    _entries     = new();
    private readonly List<DebugGraphInfo>   _graphs      = new();
    private readonly List<DebugPinInfo>     _pins        = new();
    private readonly List<StateLayoutField> _stateFields = new();
    private readonly Dictionary<Guid, (Guid GraphId, int StartLine)> _openNodes = new();
    private readonly Guid  _assetId;
    private readonly int   _blueprintId;
    private readonly ulong _structureHash;
    private string _assetName           = string.Empty;
    private string _generatedSourcePath = string.Empty;

    public DebugMapBuilder() { }

    public DebugMapBuilder(Guid assetId) { _assetId = assetId; }

    public DebugMapBuilder(Guid assetId, int blueprintId, ulong structureHash)
    {
        _assetId       = assetId;
        _blueprintId   = blueprintId;
        _structureHash = structureHash;
    }

    public void SetAssetName(string name)           => _assetName           = name;
    public void SetGeneratedSourcePath(string path) => _generatedSourcePath = path;

    public void AddGraph(DebugGraphInfo graph)          => _graphs.Add(graph);
    public void AddPin(DebugPinInfo pin)                => _pins.Add(pin);
    public void AddStateLayoutField(StateLayoutField f) => _stateFields.Add(f);

    public void Record(Guid nodeId, Guid graphId, int startLine, int endLine)
        => _entries.Add(new DebugMapEntry(nodeId, graphId, startLine, endLine));

    public void RecordNodeStart(Guid nodeId, Guid graphId, int line)
    {
        if (!_openNodes.ContainsKey(nodeId))
            _openNodes[nodeId] = (graphId, line);
    }

    public void RecordNodeEnd(Guid nodeId, int line)
    {
        if (!_openNodes.TryGetValue(nodeId, out var info)) return;
        _openNodes.Remove(nodeId);
        Record(nodeId, info.GraphId, info.StartLine, line);
    }

    public DebugMap Build() => new DebugMap
    {
        AssetId             = _assetId,
        AssetName           = _assetName,
        BlueprintId         = _blueprintId,
        StructureHash       = _structureHash,
        GeneratedSourcePath = _generatedSourcePath,
        Entries             = _entries.AsReadOnly(),
        Graphs              = _graphs.AsReadOnly(),
        Pins                = _pins.AsReadOnly(),
        StateLayout         = new DebugStateLayout { Fields = _stateFields.AsReadOnly() },
    };
}
