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
            new("MoveTo",           "Fdp.Toolkit.Behavior.Components.LocomotionChannel", 1, "System.Int32"),
            new("FollowRoute",      "Fdp.Toolkit.Behavior.Components.LocomotionChannel", 3, "System.Int32"),
            new("AimAndFire",       "Fdp.Toolkit.Behavior.Components.WeaponChannel",     1, "System.Int32"),
            new("OpenDoor",         "Fdp.Toolkit.Behavior.Components.InteractionChannel", 4, "System.Int32"),
            new("EjectPassengers",  "Fdp.Toolkit.Behavior.Components.InteractionChannel", 3, "System.Int32"),
        };
}
