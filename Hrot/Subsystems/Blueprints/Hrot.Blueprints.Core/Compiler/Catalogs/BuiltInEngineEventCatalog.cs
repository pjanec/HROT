using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Perception.Events;

namespace Hrot.Blueprints.Core.Compiler.Catalogs;

public sealed class BuiltInEngineEventCatalog : IEngineEventCatalog
{
    public static readonly BuiltInEngineEventCatalog Instance = new();

    public IReadOnlyList<EngineEventCatalogEntry> GetEntries() =>
        new List<EngineEventCatalogEntry>
        {
            new("HitEvent",              typeof(HitEvent)),
            new("BehaviorFinishedEvent", typeof(BehaviorFinishedEvent)),
            new("TargetVisibleEvent",    typeof(TargetVisibleEvent)),
        };
}
