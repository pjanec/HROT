using System;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.Editor.AiShared.Emit;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;

namespace Hrot.Hsm.Editor.Emit;

/// <summary>
/// Thin adapter: maps the editor model to the persisted DTO and delegates
/// to the <see cref="HsmEmitCore"/> for deterministic C# emission.
/// Design §6.1: the emit core (netstandard2.0) holds all deterministic emission logic;
/// this class is the net8 adapter.
/// Public behavior is unchanged: callers (AiAssetEmitService etc.) keep working.
/// </summary>
public sealed class HsmFluentEmitter : IFluentCSharpEmitter<HsmAsset>
{
    /// <summary>
    /// Emits the complete .cs file content for the given HSM asset.
    /// Delegates to <see cref="HsmEmitCore.Emit"/> via the mapper.
    /// </summary>
    public string Emit(HsmAsset asset)
    {
        var dto = HsmAssetMapper.ToDto(asset);
        return HsmEmitCore.Emit(dto, LaneResolver());
    }

    /// <summary>
    /// ⭐ <c>HSM-020</c> — "what <c>CommandLane</c> does this action write to?", for the EDITOR's save path.
    ///
    /// <para>The generator answers the same question from the Roslyn compilation
    /// (<c>HsmActionLaneResolver</c>); here the behaviour assemblies are already loaded, so the answer comes from
    /// the dictionary <c>HsmOutputLaneMaskInferrer</c> already builds for the inspector and the conflict rule.
    /// ⭐ One rule, two hosts — the editor's emitted file and the build's generated file must declare the same
    /// lanes or the two would disagree about the same asset.</para>
    ///
    /// <para>⚠ Built per emit, not cached: a hot reload replaces the assemblies, and a stale lane map would bake
    /// a mask the running code no longer agrees with. Saving is not a hot path.</para>
    /// </summary>
    private static Func<string, byte?> LaneResolver()
    {
        var laneMap = Hrot.Hsm.Editor.Validation.HsmOutputLaneMaskInferrer.BuildLaneDictionaryFromLoadedAssemblies();
        return fqn => laneMap.TryGetValue(fqn, out var lane) ? (byte)lane : null;
    }
}
