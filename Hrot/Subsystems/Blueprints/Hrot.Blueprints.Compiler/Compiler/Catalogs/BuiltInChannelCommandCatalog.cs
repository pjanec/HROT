namespace Hrot.Blueprints.Core.Compiler.Catalogs;

public sealed class BuiltInChannelCommandCatalog : IChannelCommandCatalog
{
    public static readonly BuiltInChannelCommandCatalog Instance = new();

    // Action names are short unqualified strings (e.g. "MoveTo", "AimAndFire") rather than
    // the hierarchical paths in the design doc (e.g. "Locomotion/MoveTo", "Weapon/AimAndFire").
    // This is intentional: the short names are the authoritative ActionId strings stored in
    // Blueprint JSON assets and matched by the runtime validator. Changing to hierarchical paths
    // would require a coordinated migration of all authored assets. (DEBT-023)
    public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() =>
        new List<ChannelCommandCatalogEntry>
        {
            // ParamsTypeFqn: real executor-param struct FQN so NodePinSchema projects rich per-field
            // data-IN pins in the editor (net8).  The netstandard2.0 generator host does not load
            // Fdp.Toolkits; NodePinSchema degrades gracefully to a single typed pin (BCF-D03).
            new("MoveTo",           "Fdp.Toolkit.Behavior.Components.LocomotionChannel",  1, "Fdp.Toolkit.Navigation.MoveToParams"),
            new("FollowRoute",      "Fdp.Toolkit.Behavior.Components.LocomotionChannel",  3, "Fdp.Toolkit.Navigation.FollowRouteParams"),
            new("AimAndFire",       "Fdp.Toolkit.Behavior.Components.WeaponChannel",      1, "Fdp.Toolkit.Combat.Executors.AimAndFireParams"),
            new("OpenDoor",         "Fdp.Toolkit.Behavior.Components.InteractionChannel", 4, "Fdp.Toolkit.Behavior.Executors.OpenDoorParams"),
            // EjectPassengers has no executor-param struct: the executor reads PassengerBuffer
            // directly from the entity.  Leave System.Int32 as a safe no-op placeholder so
            // NodePinSchema emits a single value pin (exec-only degradation path).
            new("EjectPassengers",  "Fdp.Toolkit.Behavior.Components.InteractionChannel", 3, "System.Int32"),
        };
}
