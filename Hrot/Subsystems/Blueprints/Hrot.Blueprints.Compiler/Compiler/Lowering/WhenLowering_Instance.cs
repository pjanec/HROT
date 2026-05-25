using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

/// <summary>
/// Stage 6 lowering: adds synthesized _when_&lt;id8&gt;_prev fields to
/// the Instance asset's Variables list for each WhenNode in ValueChanged or ConditionMet mode.
/// (EventFired nodes have no synthesized state.)
/// Field layout and StructureHash are computed by the caller (Stage6_Lower) after this.
/// </summary>
internal static class WhenLowering_Instance
{
    private static readonly IrTypeRef FloatType =
        new IrTypeRef { FullName = "System.Single", IsUnmanaged = true, SizeBytes = 4 };

    private static readonly IrTypeRef BoolType =
        new IrTypeRef { FullName = "System.Boolean", IsUnmanaged = true, SizeBytes = 1 };

    public static IrAsset Apply(IrAsset asset)
    {
        // Collect all synthesized field names that need to be added.
        var toAdd = new List<IrField>();
        var seen  = new HashSet<string>();

        foreach (var graph in asset.Graphs)
        foreach (var block in graph.Blocks)
        foreach (var stmt  in block.Statements)
        {
            if (stmt.Operation is IrOp_WhenValueChangedCheck vc)
            {
                if (!seen.Add(vc.SynthFieldName)) continue;
                var fieldId = SynthesizedGuids.WhenPrevField(asset.AssetId,
                    DeriveNodeIdFromFieldName(vc.SynthFieldName));
                toAdd.Add(new IrField
                {
                    Id                 = fieldId,
                    Name               = vc.SynthFieldName,
                    Type               = FloatType,   // M2 scope: float only (scalar Value Changed)
                    DefaultValueCSharp = "default",
                });
            }
            else if (stmt.Operation is IrOp_WhenConditionMetCheck cm)
            {
                if (!seen.Add(cm.SynthFieldName)) continue;
                var fieldId = SynthesizedGuids.WhenPrevField(asset.AssetId,
                    DeriveNodeIdFromFieldName(cm.SynthFieldName));
                toAdd.Add(new IrField
                {
                    Id                 = fieldId,
                    Name               = cm.SynthFieldName,
                    Type               = BoolType,
                    DefaultValueCSharp = "default",
                });
            }
            else if (stmt.Operation is IrOp_WhenEqsResultCheck eqs)
            {
                if (!seen.Add(eqs.SynthFieldName)) continue;
                var fieldId = SynthesizedGuids.WhenPrevField(asset.AssetId,
                    DeriveNodeIdFromFieldName(eqs.SynthFieldName));
                toAdd.Add(new IrField
                {
                    Id                 = fieldId,
                    Name               = eqs.SynthFieldName,
                    Type               = new IrTypeRef
                    {
                        FullName    = eqs.SynthStructTypeName, // local generated type (starts with '_')
                        IsUnmanaged = true,
                        SizeBytes   = eqs.SynthStructSizeBytes,
                    },
                    DefaultValueCSharp = "default",
                });
            }
            else
            {
                continue;
            }
        }

        if (toAdd.Count == 0) return asset;

        // Append synthesized fields after declared variables; deterministic order by name.
        toAdd.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        var newVariables = asset.Variables.Concat(toAdd).ToList();
        return asset with { Variables = newVariables };
    }

    /// <summary>
    /// Reconstructs a stable Guid proxy from "_when_&lt;id8&gt;_prev".
    /// Uses the 8-char hex prefix to derive the Guid.
    /// </summary>
    private static Guid DeriveNodeIdFromFieldName(string synthFieldName)
    {
        // synthFieldName = "_when_<8hex>_prev"
        // Extract the 8 hex chars between "_when_" and "_prev"
        const string prefix = "_when_";
        const string suffix = "_prev";
        if (synthFieldName.StartsWith(prefix) && synthFieldName.EndsWith(suffix))
        {
            var hex = synthFieldName.Substring(prefix.Length,
                synthFieldName.Length - prefix.Length - suffix.Length);
            if (hex.Length == 8)
            {
                // Pad to a valid Guid string
                return new Guid(hex.PadRight(32, '0'));
            }
        }
        return Guid.Empty;
    }
}
