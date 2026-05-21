namespace Hrot.Blueprints.Core.Compiler.Catalogs;

// Stub implementation - channel command types populated in CP-004.
// Lives in Hrot.Blueprints.Core rather than Fdp.Toolkits to avoid circular project reference.
public sealed class BuiltInChannelCommandCatalog : IChannelCommandCatalog
{
    public static readonly BuiltInChannelCommandCatalog Instance = new();

    public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() =>
        new List<ChannelCommandCatalogEntry>();
}
