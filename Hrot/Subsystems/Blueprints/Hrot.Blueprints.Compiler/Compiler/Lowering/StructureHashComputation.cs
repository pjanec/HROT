using System.Text;
using Hrot.Blueprints.Core.Compiler.Determinism;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

internal static class StructureHashComputation
{
    public static ulong Compute(IrAsset asset)
    {
        var sb = new StringBuilder();
        sb.Append(asset.Dispatch).Append(';');
        AppendFields(sb, asset.Parameters);
        // ⭐⭐ Batch 86 — the ONE state tier (R-01). ⚠⚠ BYTE-IDENTICAL to the old
        //    `WorkingState ++ Variables`: IrAsset.WorkingState is now always empty, so the append
        //    sequence is unchanged for every asset. 🔴 R-24 lives on this line.
        // ⛔⛔ Batch 85 replaced the FIRST line and left the SECOND — the state fields hashed twice and
        //    24 of 43 hashes moved. ONE call. Gate 8 exists because of exactly this.
        AppendFields(sb, asset.StateDeclarations);
        // BP-57 / ⭐⭐ Q27-A3 — a blackboard-resident local occupies real blackboard bytes, so it MUST
        // be here. The emitted BTreeTick wipes and re-initialises that memory only when
        // `storedHash != StructureHash`; a slot outside the hash would let a changed type or layout
        // reinterpret the previous run's bytes, with nothing reporting it.
        AppendFields(sb, asset.GraphLocalSlots);
        return FnvHasher.Hash64(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static void AppendFields(StringBuilder sb, IReadOnlyList<IrField> fields)
    {
        foreach (var f in fields)
            sb.Append(f.Name).Append('|')
              .Append(f.Type.FullName).Append('|')
              .Append(f.Offset).Append('|')
              .Append(f.Size).Append(';');
    }
}
