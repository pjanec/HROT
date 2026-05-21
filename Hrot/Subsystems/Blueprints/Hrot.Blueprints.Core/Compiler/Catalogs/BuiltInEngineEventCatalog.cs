namespace Hrot.Blueprints.Core.Compiler.Catalogs;

// Stub implementation - engine event types populated in CP-004.
// Lives in Hrot.Blueprints.Core rather than Fdp.Toolkits to avoid circular project reference.
public sealed class BuiltInEngineEventCatalog : IEngineEventCatalog
{
    public static readonly BuiltInEngineEventCatalog Instance = new();

    public IReadOnlyList<EngineEventCatalogEntry> GetEntries() =>
        new List<EngineEventCatalogEntry>();
}
