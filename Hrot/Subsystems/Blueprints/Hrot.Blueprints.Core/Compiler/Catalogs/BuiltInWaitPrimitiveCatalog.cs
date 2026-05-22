using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Navigation;

namespace Hrot.Blueprints.Core.Compiler.Catalogs;

public sealed class BuiltInWaitPrimitiveCatalog : IWaitPrimitiveCatalog
{
    public static readonly BuiltInWaitPrimitiveCatalog Instance = new();

    public IReadOnlyList<WaitPrimitiveCatalogEntry> GetEntries() =>
        new List<WaitPrimitiveCatalogEntry>
        {
            new("WaitForChannel:Locomotion",           WaitKind.Channel,          typeof(LocomotionChannel)),
            new("WaitForChannel:Weapon",               WaitKind.Channel,          typeof(WeaponChannel)),
            new("WaitForChannel:Interaction",          WaitKind.Channel,          typeof(InteractionChannel)),
            new("WaitForEvent:BehaviorFinishedEvent",  WaitKind.Event,            typeof(BehaviorFinishedEvent)),
            new("WaitForRingBufferResult:Pathfinding", WaitKind.RingBufferResult, typeof(PathfindingBatchData)),
        };
}
