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
        return HsmEmitCore.Emit(dto);
    }
}
