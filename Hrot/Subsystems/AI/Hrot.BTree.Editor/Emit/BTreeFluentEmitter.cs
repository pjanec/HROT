using Hrot.AiEditor.Persistence.Emit;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared.Emit;

namespace Hrot.BTree.Editor.Emit;

/// <summary>
/// Thin adapter: maps the editor model to the persisted DTO and delegates
/// to the <see cref="BTreeEmitCore"/> for deterministic C# emission.
/// Design §6.1: the emit core (netstandard2.0) holds all deterministic emission logic;
/// this class is the net8 adapter.
/// Public behavior is unchanged: callers (AiAssetEmitService etc.) keep working.
/// </summary>
public sealed class BTreeFluentEmitter : IFluentCSharpEmitter<BehaviorTreeAsset>
{
    /// <summary>
    /// Emits the complete .cs file content for the given BTree asset.
    /// Delegates to <see cref="BTreeEmitCore.Emit"/> via the mapper.
    /// </summary>
    public string Emit(BehaviorTreeAsset asset)
    {
        var dto = BehaviorTreeAssetMapper.ToDto(asset);
        return BTreeEmitCore.Emit(dto);
    }
}
