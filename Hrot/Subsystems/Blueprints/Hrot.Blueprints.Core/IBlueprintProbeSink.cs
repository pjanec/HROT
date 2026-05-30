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
}
