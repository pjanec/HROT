using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

internal static class FieldLayout
{
    public static IrAsset ComputeFieldLayouts(IrAsset asset)
    {
        var parameters   = LayoutFields(asset.Parameters,   startOffset: 0,  out _);
        var workingState = LayoutFields(asset.WorkingState, startOffset: 8,  out int afterWs);
        var variables    = LayoutFields(asset.Variables,    startOffset: 16, out int afterVars);

        // BP-57 / Q27-A3 — the blackboard slots backing suspending graphs' locals are emitted as
        // fields on the SAME struct as the asset's own storage, immediately after it, so their offsets
        // continue that struct's layout: WorkingState for an AiPrimitive, State (Variables) otherwise.
        // ⚠ They are laid out but NOT merged into those lists — see IrAsset.GraphLocalSlots.
        int slotStart = asset.Dispatch == BlueprintDispatchKind.AiPrimitive ? afterWs : afterVars;

        return asset with
        {
            Parameters      = parameters,
            WorkingState    = workingState,
            Variables       = variables,
            GraphLocalSlots = LayoutFields(asset.GraphLocalSlots, slotStart, out _),
        };
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
