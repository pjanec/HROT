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

    /// <summary>
    /// ⭐⭐⭐ <b>Ruling 8 (Batch 56) — the ONE state tier: <c>WorkingState ∪ Variables</c>.</b>
    ///
    /// <para>
    /// ⭐ <b>The user's words:</b> <i>"as the global vars and working state vars are the same stuff, it
    /// makes no sense to emit them differently… no keeping two implementations for the same concept."</i>
    /// Both kinds are the cell <c>(State, Asset)</c>; only <see cref="Parameters"/> — <c>(Input, Asset)</c>
    /// — is a genuinely different thing, and it stays out.
    /// </para>
    ///
    /// <para>
    /// ⛔⛔ <b>What this closes.</b> <c>U-12</c> made the mixture legal at Stage 2 (<c>BP1024</c> retired,
    /// <c>BP1031</c> split) and <c>Stage5.FindVariableRef</c> already resolves across both — but the two
    /// emitters each walked ONE list, so a wrong-side declaration either produced a Roslyn error naming a
    /// field the designer never wrote, or — unreferenced — 🔴 <b>vanished silently while its initial value
    /// sat in the JSON.</b> ⭐ Every reader of the state tier now walks THIS, so there is one answer.
    /// </para>
    ///
    /// <para>
    /// ⚠⚠ <b>The order is <c>DeclarationList.KindOrder</c> — WorkingState, then Variable</b>: storage
    /// order, which is also <c>StructureHashComputation</c>'s append order and the order
    /// <see cref="Assets.BlueprintAsset.DeclarationStore"/> keeps its runs in. ⛔ Not resolution order
    /// (<c>Variables</c> first), which is a name-collision <i>priority</i> and would put field order out of
    /// step with the hash.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>The three lists remain the STORAGE and this is a projection over them</b> — deliberately, per
    /// the batch's IR-boundary constraint. <c>VariableRef</c> addresses a (kind, list-relative index), so
    /// collapsing the storage would invalidate every baked reference; a union that only ever describes
    /// <i>layout</i> costs nothing and settles the question the emitters were disagreeing about.
    /// ⚠ <b>Not cached, on purpose:</b> this is a <c>record</c>, and a cached field would survive a
    /// <c>with</c> expression and go stale exactly when a lowering pass appends a field. The two
    /// single-list fast paths mean every shipped asset allocates nothing at all.
    /// </para>
    /// </summary>
    public IReadOnlyList<IrField> StateDeclarations
    {
        get
        {
            if (WorkingState.Count == 0) return Variables;
            if (Variables.Count    == 0) return WorkingState;
            var all = new List<IrField>(WorkingState.Count + Variables.Count);
            all.AddRange(WorkingState);
            all.AddRange(Variables);
            return all;
        }
    }

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
