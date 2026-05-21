using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

internal static class FieldLayout
{
    public static IrAsset ComputeFieldLayouts(IrAsset asset)
    {
        return asset with
        {
            Parameters   = LayoutFields(asset.Parameters,   startOffset: 0),
            WorkingState = LayoutFields(asset.WorkingState, startOffset: 8),
            Variables    = LayoutFields(asset.Variables,    startOffset: 16),
        };
    }

    private static IReadOnlyList<IrField> LayoutFields(
        IReadOnlyList<IrField> fields, int startOffset)
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
        return result;
    }

    private static int TypeAlignment(IrTypeRef t)
        => t.SizeBytes switch { 1 => 1, 2 => 2, <= 4 => 4, _ => 8 };

    private static int AlignUp(int offset, int align)
        => (offset + align - 1) & ~(align - 1);
}
