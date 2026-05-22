using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Stages;

/// <summary>
/// Output of Stage 4 -- TypeResolve. Holds the original asset plus resolved type maps.
/// </summary>
internal sealed record TypedAsset(
    BlueprintAsset Asset,
    IReadOnlyDictionary<Guid, IrTypeRef> PinTypes,
    IReadOnlyDictionary<Guid, IrTypeRef> FieldTypes);
