namespace Hrot.Blueprints.Core.Compiler.Catalogs;

// Stub implementation - wait primitive types populated in CP-004.
// Lives in Hrot.Blueprints.Core rather than Fdp.Toolkits to avoid circular project reference.
public sealed class BuiltInWaitPrimitiveCatalog : IWaitPrimitiveCatalog
{
    public static readonly BuiltInWaitPrimitiveCatalog Instance = new();

    public IReadOnlyList<WaitPrimitiveCatalogEntry> GetEntries() =>
        new List<WaitPrimitiveCatalogEntry>();
}
