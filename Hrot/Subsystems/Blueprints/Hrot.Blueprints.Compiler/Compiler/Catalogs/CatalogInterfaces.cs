namespace Hrot.Blueprints.Core.Compiler.Catalogs;

public sealed record EngineEventCatalogEntry(string Name, string EventTypeFqn);

public sealed record ChannelCommandCatalogEntry(
    string Name, string ChannelTypeFqn, ushort ActionId, string ParamsTypeFqn);

public enum WaitKind { Channel, Event, RingBufferResult }

public sealed record WaitPrimitiveCatalogEntry(
    string Name, WaitKind Kind, string TargetTypeFqn);

public interface IEngineEventCatalog
{
    IReadOnlyList<EngineEventCatalogEntry> GetEntries();
}

public interface IChannelCommandCatalog
{
    IReadOnlyList<ChannelCommandCatalogEntry> GetEntries();
}

public interface IWaitPrimitiveCatalog
{
    IReadOnlyList<WaitPrimitiveCatalogEntry> GetEntries();
}
