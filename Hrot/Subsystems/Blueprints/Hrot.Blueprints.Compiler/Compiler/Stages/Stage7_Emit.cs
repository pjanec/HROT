using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Stages;

internal static class Stage7_Emit
{
    public static (string GeneratedSource, DebugMap DebugMap) Run(
        IrAsset asset, CompilerMode mode, DiagnosticSink sink,
        IReadOnlyList<BlueprintSignature>? siblingSignatures = null, string? behaviorGroup = null)
    {
        CheckChannelsAreEngineChannels(asset, sink);
        var ctx = new EmissionContext(asset, mode, siblingSignatures) { BehaviorGroup = behaviorGroup };
        var emitter = new CSharpEmitter(ctx);
        return emitter.Emit(asset);
    }

    /// <summary>
    /// ⭐ <c>CE-1435</c> — a blueprint AI primitive's exit cleanup releases its derived channels through the runtime <c>ChannelRelease</c>, which knows the THREE engine channels (Locomotion, Weapon,
    /// Interaction). 📐 Measured <c>2026-10-30</c>: <c>BuiltInChannelCommandCatalog</c> names only those three, and no production <c>IChannelCommandCatalog</c> exists beside it (the editor's
    /// attribute-discovered catalog is a placeholder), so a channel component outside them can only come from a hand-authored asset or a test double — the emitter used to write the two release
    /// statements inline for it (a second copy of the rule). It is REFUSED instead: <c>BP6001</c>, naming the component.
    /// </summary>
    internal static void CheckChannelsAreEngineChannels(IrAsset asset, DiagnosticSink sink)
    {
        // only an asset hosted as an HSM action has the exit-cleanup thunk (the fallback's whole reach)
        if (asset.Dispatch != BlueprintDispatchKind.AiPrimitive || !asset.Hostings.Contains(AiPrimitiveHosting.HsmAction)) return;
        foreach (var fqn in BlueprintChannelDerivation.Derive(asset).ChannelComponentFqns)
            if (!AiPrimitiveEmitter.IsEngineChannel(fqn))
                sink.Add(Diagnostic.Error(DiagnosticCodes.BP6001,
                    $"A channel command names '{fqn}', which is not an engine channel (Locomotion, Weapon, Interaction): the exit cleanup cannot release it. Use an engine channel, or give the component a ChannelKind in ChannelRelease first (CE-1435).",
                    asset.AssetId));
    }
}
