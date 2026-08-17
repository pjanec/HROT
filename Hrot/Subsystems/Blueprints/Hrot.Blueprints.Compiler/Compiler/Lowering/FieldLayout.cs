using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

internal static class FieldLayout
{
    /// <summary>
    /// ⭐ The base offset of the asset's ONE state struct: an AiPrimitive's <c>WorkingState</c> sits at
    /// <c>memory + 8</c> in the blackboard (<c>AiPrimitiveEmitter</c>'s thunks), while an Instance's
    /// <c>State</c> opens with a 16-byte <c>BlueprintLatentCursor</c> (<c>InstanceEmitter</c>).
    /// ⭐⭐ Batch 70 — for an Instance the params now sit BETWEEN the two, so the state base is
    /// <c>16 + N</c> rather than a constant. See <see cref="ParamsStructBase"/>.
    /// </summary>
    private static int StateStructBase(IrAsset asset, int afterParams)
        => asset.Dispatch == BlueprintDispatchKind.AiPrimitive ? 8 : afterParams;

    /// <summary>
    /// ⭐⭐⭐ Batch 70 / <c>DESIGN_Parameter_Model.md</c> §3.3 — <b>where an asset's params begin.</b>
    ///
    /// <para>
    /// ⛔⛔ It used to be a literal <c>0</c> for BOTH dispatch kinds, and for an Instance <b>offset 0
    /// IS the <c>BlueprintLatentCursor</c></b> — the design names this the <c>startOffset: 0</c> trap.
    /// It was safe only because no shipped Instance carries parameters; the moment one did, resolving
    /// its params would have overwritten the latent cursor and looked like a scheduler bug.
    /// </para>
    ///
    /// <para>
    /// ⭐ The Instance payload is ONE struct — <c>[Cursor 16][Params N][State M]</c> — so
    /// <c>StateSize</c> keeps meaning "the whole payload" and <c>TryAttach</c>/<c>ChooseTier</c> need
    /// no new arithmetic. 📐 <b>Byte-identical for all 296 shipped Instance assets: ZERO carry
    /// parameters, so <c>N = 0</c> and <c>16 + N == 16</c>.</b>
    /// </para>
    /// </summary>
    internal static int ParamsStructBase(IrAsset asset)
        => asset.Dispatch == BlueprintDispatchKind.AiPrimitive ? 0 : BlueprintLatentCursorSize;

    /// <summary>The 16 bytes an Instance payload opens with. ⭐ This constant has ONE home.</summary>
    internal const int BlueprintLatentCursorSize = 16;

    public static IrAsset ComputeFieldLayouts(IrAsset asset)
    {
        // Parameters are the OTHER cell — (Input, Asset) — and their own packed region: at offset 0 for
        // an AiPrimitive (its own `Params` struct), at 16 for an Instance (inside `State`, after the
        // cursor).
        var parameters = LayoutFields(asset.Parameters, ParamsStructBase(asset), out int afterParams);

        // ⭐⭐ Batch 56 / ruling 8 — ONE layout run over IrAsset.StateDeclarations, because the two kinds
        // land in ONE struct. ⛔ They used to be laid out INDEPENDENTLY, from 8 and from 16, which is only
        // consistent while at most one of them is populated: a mixed asset got two overlapping offset runs
        // describing fields the emitter then wrote out consecutively. ⚠ Wrong offsets are worse than none —
        // they read plausible bytes from the wrong place — and StructureHash bakes them, so this is the
        // half of the unification that has to move with the emitters rather than after them.
        //
        // ⭐ Byte-identical for every shipped asset: 0 of 458 carry both kinds, so the union IS the single
        // populated list and its base is that list's old start (WorkingState @8 for an AiPrimitive,
        // Variables @16 for an Instance).
        var laid = LayoutFields(asset.StateDeclarations, StateStructBase(asset, afterParams), out int afterState);

        // Split the one run back into the two windows VariableRef addresses (kind + list-relative index).
        // ⚠ The order here must stay DeclarationList.KindOrder — see IrAsset.StateDeclarations.
        var workingState = Slice(laid, 0, asset.WorkingState.Count);
        var variables    = Slice(laid, asset.WorkingState.Count, asset.Variables.Count);

        // BP-57 / Q27-A3 — the blackboard slots backing suspending graphs' locals are emitted as
        // fields on the SAME struct as the asset's own storage, immediately after it, so their offsets
        // continue that struct's layout.
        // ⚠ They are laid out but NOT merged into those lists — see IrAsset.GraphLocalSlots.
        return asset with
        {
            Parameters      = parameters,
            WorkingState    = workingState,
            Variables       = variables,
            GraphLocalSlots = LayoutFields(asset.GraphLocalSlots, afterState, out _),
        };
    }

    private static IReadOnlyList<IrField> Slice(IReadOnlyList<IrField> all, int start, int count)
    {
        if (count == 0) return Array.Empty<IrField>();
        if (start == 0 && count == all.Count) return all;
        var result = new List<IrField>(count);
        for (int i = 0; i < count; i++) result.Add(all[start + i]);
        return result;
    }

    private static IReadOnlyList<IrField> LayoutFields(
        IReadOnlyList<IrField> fields, int startOffset, out int endOffset)
    {
        var result = new List<IrField>(fields.Count);
        int offset = startOffset;
        foreach (var f in fields)
        {
            int align = TypeAlignment(f.Type);
            offset = AlignUp(offset, align);
            result.Add(f with { Offset = offset, Size = f.Type.SizeBytes });
            offset += f.Type.SizeBytes;
        }
        endOffset = offset;
        return result;
    }

    private static int TypeAlignment(IrTypeRef t)
        => t.SizeBytes switch { 1 => 1, 2 => 2, <= 4 => 4, _ => 8 };

    private static int AlignUp(int offset, int align)
        => (offset + align - 1) & ~(align - 1);
}
