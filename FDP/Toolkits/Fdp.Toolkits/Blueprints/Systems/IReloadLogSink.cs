namespace Fdp.Toolkit.Blueprints.Systems;

/// <summary>Called by BlueprintTickSystem when a hard-reload reset occurs for a slot.</summary>
public interface IReloadLogSink
{
    void OnHardReset(int blueprintId, uint newInstanceVersion);
}

/// <summary>No-op singleton implementation.</summary>
public sealed class NullReloadLogSink : IReloadLogSink
{
    public static readonly NullReloadLogSink Instance = new();
    private NullReloadLogSink() { }
    public void OnHardReset(int blueprintId, uint newInstanceVersion) { }
}
