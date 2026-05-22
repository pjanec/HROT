using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Lowering;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Core.Compiler.Stages;

internal static class Stage6_Lower
{
    public static IrAsset Run(IrAsset asset, CompilerMode mode, DiagnosticSink sink)
    {
        // Dispatch-specific lowering first so that synthesized fields such as
        // __phase are added to WorkingState before FieldLayout assigns offsets.
        asset = asset.Dispatch switch
        {
            AssetDispatchKind.Library     => LibraryLowering.Apply(asset, sink),
            AssetDispatchKind.AiPrimitive => AiPrimitiveLowering.Apply(asset, sink),
            AssetDispatchKind.Instance    => InstanceLowering.Apply(asset, sink),
            _ => throw new InvalidOperationException($"Unknown dispatch kind: {asset.Dispatch}")
        };

        // Assign Offset/Size to all fields now that synthesized fields are present.
        asset = FieldLayout.ComputeFieldLayouts(asset);

        // Compute structure hash after final layout.
        asset = asset with { StructureHash = StructureHashComputation.Compute(asset) };

        // Insert debug probes last (targets the final block structure).
        asset = DebugProbeInsertion.Apply(asset, mode);

        return asset;
    }
}

