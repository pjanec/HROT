namespace Hrot.Blueprints.Core.Compiler.Catalogs;

public sealed record EngineEventCatalogEntry(string Name, Type EventType);

public sealed record ChannelCommandCatalogEntry(
    string Name, Type ChannelType, ushort ActionId, Type ParamsType);

public enum WaitKind { Channel, Event, RingBufferResult }

public sealed record WaitPrimitiveCatalogEntry(
    string Name, WaitKind Kind, Type TargetType);

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
