namespace Hrot.Editor.AiShared.Debug;

/// <summary>
/// Tracks registered debug sessions by asset ID and reports live entity counts.
/// Sessions should register themselves when attached to a specific asset,
/// and unregister when detached.
/// </summary>
public sealed class LiveSessionRegistry : ILiveSessionProvider
{
    private readonly Dictionary<Guid, IAiDebugSession> _sessions = new();

    /// <summary>Registers a session for the given asset. Overwrites any previous registration.</summary>
    public void Register(Guid assetId, IAiDebugSession session)
        => _sessions[assetId] = session;

    /// <summary>Removes the registration for the given asset.</summary>
    public void Unregister(Guid assetId)
        => _sessions.Remove(assetId);

    public int GetActiveEntityCount(Guid assetId)
        => _sessions.TryGetValue(assetId, out var s) && s.IsAttached ? 1 : 0;
}
