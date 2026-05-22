namespace Hrot.Blueprints.Core.Compiler.Catalogs;

public sealed class BuiltInChannelCommandCatalog : IChannelCommandCatalog
{
    public static readonly BuiltInChannelCommandCatalog Instance = new();

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
