namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Runtime dispatch kind for a compiled Blueprint definition.
/// Values match Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.
/// Mirror of <c>Hrot.Blueprints.Core.Assets.BlueprintDispatchKind</c>.
/// Both enums are kept in sync manually because the dependency direction
/// prevents Fdp.Toolkits from referencing Hrot.Blueprints.Core.
/// </summary>
public enum BlueprintDispatchKind
{
    Library    = 0,
    AiPrimitive = 1,
    Instance   = 2,
}
