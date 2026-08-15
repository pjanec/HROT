using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

internal static class FieldLayout
{
    /// <summary>
    /// ⭐ The base offset of the asset's ONE state struct: an AiPrimitive's <c>WorkingState</c> sits at
    /// <c>memory + 8</c> in the blackboard (<c>AiPrimitiveEmitter</c>'s thunks), while an Instance's
    /// <c>State</c> opens with a 16-byte <c>BlueprintLatentCursor</c> (<c>InstanceEmitter</c>).
    /// </summary>
    private static int StateStructBase(IrAsset asset)
        => asset.Dispatch == BlueprintDispatchKind.AiPrimitive ? 8 : 16;

    public static IrAsset ComputeFieldLayouts(IrAsset asset)
    {
        // Parameters are the OTHER cell — (Input, Asset) — and their own packed struct at offset 0.
        var parameters = LayoutFields(asset.Parameters, startOffset: 0, out _);

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
        var laid = LayoutFields(asset.StateDeclarations, StateStructBase(asset), out int afterState);

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
