namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Runtime dispatch kind for a compiled Blueprint definition.
/// Values match Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.
/// </summary>
public enum BlueprintDispatchKind
{
    Library    = 0,
    AiPrimitive = 1,
    Instance   = 2,
}
