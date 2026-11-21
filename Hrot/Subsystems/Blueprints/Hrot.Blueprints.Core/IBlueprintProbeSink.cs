using Fdp.Core;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Thin probe sink that generated Blueprint code calls into via DebugProbe.
/// </summary>
public interface IBlueprintProbeSink
{
    void OnNodeEnter(Entity self, string nodeId);
    void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged;
    /// <summary>Called when generated code enters a peer Blueprint call. peerAssetIdString is a Guid in "D" format.</summary>
    void OnPeerCallEnter(Entity self, string peerAssetIdString, string methodName);
    /// <summary>Called when generated code exits a peer Blueprint call. peerAssetIdString is a Guid in "D" format.</summary>
    void OnPeerCallExit(Entity self, string peerAssetIdString, string methodName);

    /// <summary>
    /// FC-0 (Fixed Collections, Q#20) -- called when a collection mutation op is refused: the
    /// never-silent half of the false-on-overflow contract. <paramref name="op"/> is the verb
    /// ("Add"/"SetAt"/...); <paramref name="reason"/> distinguishes the cause
    /// ("component-absent" -- the write-if-present guard failed; "op-rejected" -- the accessor
    /// returned false: full / index out of range / bad length). Default-implemented no-op so
    /// existing sinks (and tests) keep compiling; sessions override to surface the diagnostic.
    /// </summary>
    void OnCollectionWriteFailed(Entity self, string nodeId, string op, string reason) { }
}
