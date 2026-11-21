using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hrot.Blueprints.Core.Compiler.Emit;

public static class DebugMapSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serialize DebugMap to JSON. Output is deterministic for identical inputs.</summary>
    public static string Serialize(DebugMap debugMap)
    {
        // Build a deterministic DTO to control field order and sort entries
        var dto = new DebugMapDto
        {
            // ⭐ BP-83 §2.3 ruling — bump to 1.1, tolerate 1.0 on read.
            //
            // The two new fields are ADDITIVE and nullable, so this is the cheapest honest option:
            //   * A 1.0 map still loads. Its entries simply carry null provenance, which is not a
            //     degraded read but the CORRECT one — a map written before macros existed has no
            //     expanded nodes, so there is genuinely nothing to point back to.
            //   * A 1.1 map for an asset with no macros serialises byte-identically to the 1.0 map it
            //     replaces, because both fields are omitted when null. So the bump costs no churn.
            //   * Leaving it at "1.0" was the alternative and is worse: the version would then be a
            //     lie about the shape, and the first consumer to branch on it would branch wrongly.
            //     A version that does not move when the schema moves is worse than no version.
            SchemaVersion       = "1.1",
            AssetId             = debugMap.AssetId,
            AssetName           = debugMap.AssetName,
            BlueprintId         = debugMap.BlueprintId,
            StructureHash       = debugMap.StructureHash,
            GeneratedSourcePath = debugMap.GeneratedSourcePath,
            Entries = debugMap.Entries
                .OrderBy(n => n.GraphId)
                .ThenBy(n => n.StartLine)
                .Select(n => new EntryDto
                {
                    NodeId      = n.NodeId,
                    GraphId     = n.GraphId,
                    StartLine   = n.StartLine,
                    EndLine     = n.EndLine,
                    NodeKind    = n.NodeKind,
                    DisplayName = n.DisplayName,
                    PhaseIndex  = n.PhaseIndex,
                    OriginNodeId  = n.OriginNodeId,
                    OriginGraphId = n.OriginGraphId,
                })
                .ToList(),
            Graphs = debugMap.Graphs
                .Select(g => new GraphDto
                {
                    GraphId   = g.GraphId,
                    GraphName = g.GraphName,
                    GraphKind = g.GraphKind,
                })
                .ToList(),
            Pins = debugMap.Pins
                .Select(p => new PinDto
                {
                    PinId                 = p.PinId,
                    NodeId                = p.NodeId,
                    PinName               = p.PinName,
                    PinDirection          = p.PinDirection,
                    PinKind               = p.PinKind,
                    TypeFullName          = p.TypeFullName,
                    ValueAccessExpression = p.ValueAccessExpression,
                })
                .ToList(),
            StateLayout = new StateLayoutDto
            {
                Fields = debugMap.StateLayout.Fields
                    .Select(f => new StateLayoutFieldDto
                    {
                        Name        = f.Name,
                        Type        = f.Type,
                        OffsetBytes = f.OffsetBytes,
                        SizeBytes   = f.SizeBytes,
                    })
                    .ToList(),
            },
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    public static DebugMap? Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<DebugMapDto>(json, Options);
        if (dto is null) return null;

        return new DebugMap
        {
            AssetId             = dto.AssetId,
            AssetName           = dto.AssetName,
            BlueprintId         = dto.BlueprintId,
            StructureHash       = dto.StructureHash,
            GeneratedSourcePath = dto.GeneratedSourcePath,
            Entries = dto.Entries.Select(n => new DebugMapEntry(
                n.NodeId,
                n.GraphId,
                n.StartLine,
                n.EndLine)
            {
                NodeKind    = n.NodeKind,
                DisplayName = n.DisplayName,
                PhaseIndex  = n.PhaseIndex,
                // Absent in a 1.0 map -- and null is exactly right there: a map written before macros
                // existed has no expanded nodes, so there is no provenance to lose.
                OriginNodeId  = n.OriginNodeId,
                OriginGraphId = n.OriginGraphId,
            }).ToList(),
            Graphs = dto.Graphs.Select(g => new DebugGraphInfo(
                g.GraphId, g.GraphName, g.GraphKind)).ToList(),
            Pins = dto.Pins.Select(p => new DebugPinInfo(
                p.PinId, p.NodeId, p.PinName, p.PinDirection,
                p.PinKind, p.TypeFullName, p.ValueAccessExpression)).ToList(),
            StateLayout = dto.StateLayout != null
                ? new DebugStateLayout
                {
                    Fields = dto.StateLayout.Fields
                        .Select(f => new StateLayoutField(f.Name, f.Type, f.OffsetBytes, f.SizeBytes))
                        .ToList(),
                }
                : new DebugStateLayout(),
        };
    }

    private sealed class DebugMapDto
    {
        public string? SchemaVersion       { get; set; }
        public Guid    AssetId             { get; set; }
        public string  AssetName           { get; set; } = string.Empty;
        public int     BlueprintId         { get; set; }
        public ulong   StructureHash       { get; set; }
        public string  GeneratedSourcePath { get; set; } = string.Empty;
        public List<EntryDto>         Entries     { get; set; } = new();
        public List<GraphDto>         Graphs      { get; set; } = new();
        public List<PinDto>           Pins        { get; set; } = new();
        public StateLayoutDto?        StateLayout { get; set; }
    }

    private sealed class EntryDto
    {
        public Guid   NodeId      { get; set; }
        public Guid   GraphId     { get; set; }
        public int    StartLine   { get; set; }
        public int    EndLine     { get; set; }
        public string NodeKind    { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int?   PhaseIndex  { get; set; }

        // BP-83. Nullable and omitted when null, so a 1.1 map with no macros is byte-identical to
        // the 1.0 map it replaces -- the version bump alone changes no existing output.
        [System.Text.Json.Serialization.JsonIgnore(
            Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public Guid?  OriginNodeId  { get; set; }

        [System.Text.Json.Serialization.JsonIgnore(
            Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public Guid?  OriginGraphId { get; set; }
    }

    private sealed class GraphDto
    {
        public Guid   GraphId   { get; set; }
        public string GraphName { get; set; } = string.Empty;
        public string GraphKind { get; set; } = string.Empty;
    }

    private sealed class PinDto
    {
        public Guid   PinId                 { get; set; }
        public Guid   NodeId                { get; set; }
        public string PinName               { get; set; } = string.Empty;
        public string PinDirection          { get; set; } = string.Empty;
        public string PinKind               { get; set; } = string.Empty;
        public string TypeFullName          { get; set; } = string.Empty;
        public string ValueAccessExpression { get; set; } = string.Empty;
    }

    private sealed class StateLayoutDto
    {
        public List<StateLayoutFieldDto> Fields { get; set; } = new();
    }

    private sealed class StateLayoutFieldDto
    {
        public string Name        { get; set; } = string.Empty;
        public string Type        { get; set; } = string.Empty;
        public int    OffsetBytes { get; set; }
        public int    SizeBytes   { get; set; }
    }
}
