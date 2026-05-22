using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Navigation;

namespace Hrot.Blueprints.Core.Compiler.Catalogs;

public sealed class BuiltInChannelCommandCatalog : IChannelCommandCatalog
{
    public static readonly BuiltInChannelCommandCatalog Instance = new();

    public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() =>
        new List<ChannelCommandCatalogEntry>
        {
            new("MoveTo",           typeof(LocomotionChannel),  NavigationConstants.ActionIdMoveTo,          typeof(int)),
            new("FollowRoute",      typeof(LocomotionChannel),  NavigationConstants.ActionIdFollowRoute,     typeof(int)),
            new("AimAndFire",       typeof(WeaponChannel),      CombatConstants.ActionIdAimAndFire,          typeof(int)),
            new("OpenDoor",         typeof(InteractionChannel), BehaviorConstants.ActionIdOpenDoor,          typeof(int)),
            new("EjectPassengers",  typeof(InteractionChannel), BehaviorConstants.ActionIdEjectPassengers,   typeof(int)),
        };
}
