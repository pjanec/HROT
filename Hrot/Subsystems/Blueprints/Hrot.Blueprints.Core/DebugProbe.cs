using Fdp.Core;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Static dispatcher that generated Blueprint code calls.
/// Wire DebugProbe.Sink to a CapturingDebugSession in tests.
/// In production (no session), Sink defaults to NullProbeSink which is a no-op.
/// </summary>
public static class DebugProbe
{
    public static IBlueprintProbeSink Sink { get; set; } = NullProbeSink.Instance;

    public static void NodeEnter(Entity self, string nodeId)
        => Sink.OnNodeEnter(self, nodeId);

    public static void PinValueChanged<T>(Entity self, string pinId, T value)
        => Sink.OnPinValueChanged(self, pinId, value);
}

/// <summary>No-op sink used when no debug session is attached.</summary>
public sealed class NullProbeSink : IBlueprintProbeSink
{
    public static NullProbeSink Instance { get; } = new NullProbeSink();
    private NullProbeSink() { }
    public void OnNodeEnter(Entity self, string nodeId) { }
    public void OnPinValueChanged<T>(Entity self, string pinId, T value) { }
}
