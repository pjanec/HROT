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

    /// <summary>
    /// BP-82 / Q25-C2 — how many <c>GraphKind.Macro</c> graphs the SOURCE asset declared.
    ///
    /// <para>
    /// ⚠ <b>Carried forward because macros do not survive into the IR at all.</b> Stage 5 skips them
    /// (they are declarations, never compilation targets) and <see cref="IrGraphKind"/> has no Macro
    /// member, so by lowering time a macro-library asset and an empty one are indistinguishable —
    /// which made <c>BP5001</c> reject the very asset shape Q25-C2 describes. This one integer is the
    /// smallest thing that tells them apart.
    /// </para>
    /// </summary>
    public int DeclaredMacroCount { get; init; }

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

    /// <summary>
    /// BP-57 / ⭐⭐ <b>Q27-A3</b> — the blackboard slots backing suspending graphs' function-locals,
    /// with graph-qualified names, in graph declaration order.
    ///
    /// <para>
    /// ⭐ These are emitted as fields on the SAME struct as the asset's own storage — <c>WorkingState</c>
    /// for an AiPrimitive, <c>State</c> for an Instance — and they enter <c>StructureHash</c>, so
    /// changing a local's type re-initialises the blackboard instead of reinterpreting stale bytes.
    /// </para>
    ///
    /// <para>
    /// ⛔⛔ <b>They are deliberately NOT in <see cref="Variables"/>/<see cref="WorkingState"/>/<see cref="Parameters"/>.</b>
    /// Those three are a positional index space that <c>Stage5.FindVariableIndex</c> and
    /// <c>EmissionContext.VarFieldName</c> already disagree about (<c>BP-226</c>); a fourth source
    /// would make that ambiguity live. Slots are addressed by NAME only and no index ever reaches them.
    /// </para>
    /// </summary>
    public IReadOnlyList<IrField> GraphLocalSlots { get; init; } = Array.Empty<IrField>();

    // All dispatch kinds
    public IReadOnlyList<IrGraph> Graphs { get; init; } = Array.Empty<IrGraph>();
}
