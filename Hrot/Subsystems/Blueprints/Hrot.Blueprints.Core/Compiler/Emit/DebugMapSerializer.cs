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
            AssetId       = debugMap.AssetId,
            BlueprintId   = debugMap.BlueprintId,
            StructureHash = debugMap.StructureHash,
            Entries = debugMap.Entries
                .OrderBy(n => n.GraphId)
                .ThenBy(n => n.StartLine)
                .Select(n => new EntryDto
                {
                    NodeId    = n.NodeId,
                    GraphId   = n.GraphId,
                    StartLine = n.StartLine,
                    EndLine   = n.EndLine,
                })
                .ToList(),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    public static DebugMap? Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<DebugMapDto>(json, Options);
        if (dto is null) return null;

        return new DebugMap
        {
            AssetId       = dto.AssetId,
            BlueprintId   = dto.BlueprintId,
            StructureHash = dto.StructureHash,
            Entries = dto.Entries.Select(n => new DebugMapEntry(
                n.NodeId,
                n.GraphId,
                n.StartLine,
                n.EndLine)).ToList(),
        };
    }

    private sealed class DebugMapDto
    {
        public Guid   AssetId       { get; set; }
        public int    BlueprintId   { get; set; }
        public ulong  StructureHash { get; set; }
        public List<EntryDto> Entries { get; set; } = new();
    }

    private sealed class EntryDto
    {
        public Guid NodeId    { get; set; }
        public Guid GraphId   { get; set; }
        public int  StartLine { get; set; }
        public int  EndLine   { get; set; }
    }
}
