namespace Hrot.Blueprints.Core.Compiler.Catalogs;

public sealed class BuiltInEngineEventCatalog : IEngineEventCatalog
{
    public static readonly BuiltInEngineEventCatalog Instance = new();

    public IReadOnlyList<EngineEventCatalogEntry> GetEntries() =>
        new List<EngineEventCatalogEntry>
        {
            new("HitEvent",              "Fdp.Toolkit.Combat.Contracts.HitEvent"),
            new("BehaviorFinishedEvent", "Fdp.Toolkit.Behavior.Events.BehaviorFinishedEvent"),
            new("TargetVisibleEvent",    "Fdp.Toolkit.Perception.Events.TargetVisibleEvent"),
        };
}
