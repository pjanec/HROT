using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler.Ir;

public sealed record IrField
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public IrTypeRef Type { get; init; } = null!;
    public string DefaultValueCSharp { get; init; } = "";
    public string? Comment { get; init; }
    public int Offset { get; init; }
    public int Size { get; init; }
}

public sealed record IrCustomEvent
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public IReadOnlyList<IrField> Parameters { get; init; } = Array.Empty<IrField>();
}

public sealed record IrAsset
{
    public Guid AssetId { get; init; }
    public string Name { get; init; } = "";
    public string SanitizedName { get; init; } = "";
    public int BlueprintId { get; init; }
    public ulong StructureHash { get; init; }
    public BlueprintDispatchKind Dispatch { get; init; }

    // For AiPrimitive only
    public AiPrimitiveIntent? Intent { get; init; }
    public IReadOnlyList<AiPrimitiveHosting> Hostings { get; init; } = Array.Empty<AiPrimitiveHosting>();
    public IReadOnlyList<IrField> Parameters { get; init; } = Array.Empty<IrField>();
    public IReadOnlyList<IrField> WorkingState { get; init; } = Array.Empty<IrField>();

    // For Instance only
    public IReadOnlyList<IrField> Variables { get; init; } = Array.Empty<IrField>();
    public IReadOnlyList<IrCustomEvent> CustomEvents { get; init; } = Array.Empty<IrCustomEvent>();
    public IReadOnlyList<int> CallablePeerBlueprintIds { get; init; } = Array.Empty<int>();
    public bool IsWorldSingleton { get; init; }
    public Hrot.Blueprints.Core.Compiler.BlackboardTier? SelectedTier { get; init; }

    // All dispatch kinds
    public IReadOnlyList<IrGraph> Graphs { get; init; } = Array.Empty<IrGraph>();
}
