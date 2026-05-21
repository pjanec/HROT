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

    public static void PeerCallEnter(Entity self, string targetAssetName, string targetGraphName)
        => Sink?.OnPeerCallEnter(self, targetAssetName, targetGraphName);

    public static void PeerCallExit(Entity self)
        => Sink?.OnPeerCallExit(self);
}

/// <summary>No-op sink used when a non-null sink is required but no session is attached.</summary>
public sealed class NullProbeSink : IBlueprintProbeSink
{
    public static NullProbeSink Instance { get; } = new NullProbeSink();
    private NullProbeSink() { }
    public void OnNodeEnter(Entity self, string nodeId) { }
    public void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged { }
    public void OnPeerCallEnter(Entity entity, string targetAssetName, string targetGraphName) { }
    public void OnPeerCallExit(Entity entity) { }
}
