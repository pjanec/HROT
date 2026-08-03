using Fdp.Core;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Static dispatcher that generated Blueprint code calls.
/// Wire DebugProbe.Sink to a session in tests or production.
/// When Sink is null, all calls are no-ops with zero allocation.
/// </summary>
public static class DebugProbe
{
    // Nullable: no default. Generated code must not assume a sink is present.
    // Assignment is a single-reference write -- atomic on 64-bit platforms; no lock needed.
    public static IBlueprintProbeSink? Sink { get; set; }

    public static void NodeEnter(Entity self, string nodeId)
        => Sink?.OnNodeEnter(self, nodeId);

    public static void PinValueChanged<T>(Entity self, string pinId, T value)
        where T : unmanaged
        => Sink?.OnPinValueChanged(self, pinId, value);

    public static void PeerCallEnter(Entity self, string peerAssetIdString, string methodName)
        => Sink?.OnPeerCallEnter(self, peerAssetIdString, methodName);

    public static void PeerCallExit(Entity self, string peerAssetIdString, string methodName)
        => Sink?.OnPeerCallExit(self, peerAssetIdString, methodName);

    /// <summary>
    /// FC-0 (Fixed Collections, Q#20) -- generated collection-write code calls this when an op is
    /// refused (component absent / accessor returned false). All arguments are emit-time string
    /// constants -- zero allocation, and a no-op when no sink is attached, so the emitter calls it
    /// unconditionally (the "never silent" overflow contract costs nothing in production).
    /// </summary>
    public static void CollectionWriteFailed(Entity self, string nodeId, string op, string reason)
        => Sink?.OnCollectionWriteFailed(self, nodeId, op, reason);

    /// <summary>
    /// Called at the start of each simulation tick by the frame loop / fixture coordinator.
    /// Forwards to IBlueprintDebugSession.OnNewTick() when the current sink is a session.
    /// Resets the per-frame breakpoint dedup set (Debug DD §9.2).
    /// </summary>
    public static void NewTick()
        => (Sink as IBlueprintDebugSession)?.OnNewTick();
}

/// <summary>No-op sink used when a non-null sink is required but no session is attached.</summary>
public sealed class NullProbeSink : IBlueprintProbeSink
{
    public static NullProbeSink Instance { get; } = new NullProbeSink();
    private NullProbeSink() { }
    public void OnNodeEnter(Entity self, string nodeId) { }
    public void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged { }
    public void OnPeerCallEnter(Entity self, string peerAssetIdString, string methodName) { }
    public void OnPeerCallExit(Entity self, string peerAssetIdString, string methodName) { }
    public void OnCollectionWriteFailed(Entity self, string nodeId, string op, string reason) { }
}
