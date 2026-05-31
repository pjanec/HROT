using Fdp.Core;

namespace Fdp.Toolkit.Blueprints.Systems;

/// <summary>Called by BlueprintTickSystem when a reload event occurs for a slot.</summary>
public interface IReloadLogSink
{
    /// <summary>Called when a soft reload replaces behavior delegates without resetting state.</summary>
    void OnSoftReload(int blueprintId, Entity entity, ulong hash);
    /// <summary>Called when a hard reset clears the slot payload due to a structure-hash mismatch.</summary>
    void OnHardReset(int blueprintId, Entity entity, ulong oldHash, ulong newHash);
}

/// <summary>No-op singleton implementation.</summary>
public sealed class NullReloadLogSink : IReloadLogSink
{
    public static readonly NullReloadLogSink Instance = new();
    private NullReloadLogSink() { }
    public void OnSoftReload(int blueprintId, Entity entity, ulong hash) { }
    public void OnHardReset(int blueprintId, Entity entity, ulong oldHash, ulong newHash) { }
}
