namespace Hrot.Blueprints.Core.Compiler.Catalogs;

public sealed class BuiltInWaitPrimitiveCatalog : IWaitPrimitiveCatalog
{
    public static readonly BuiltInWaitPrimitiveCatalog Instance = new();

    public IReadOnlyList<WaitPrimitiveCatalogEntry> GetEntries() =>
        new List<WaitPrimitiveCatalogEntry>
        {
            new("WaitForChannel:Locomotion",           WaitKind.Channel,          "Fdp.Toolkit.Behavior.Components.LocomotionChannel"),
            new("WaitForChannel:Weapon",               WaitKind.Channel,          "Fdp.Toolkit.Behavior.Components.WeaponChannel"),
            new("WaitForChannel:Interaction",          WaitKind.Channel,          "Fdp.Toolkit.Behavior.Components.InteractionChannel"),
            new("WaitForEvent:BehaviorFinishedEvent",  WaitKind.Event,            "Fdp.Toolkit.Behavior.Events.BehaviorFinishedEvent"),
            new("WaitForRingBufferResult:Pathfinding", WaitKind.RingBufferResult, "Fdp.Toolkit.Navigation.PathfindingBatchData"),
        };
}
