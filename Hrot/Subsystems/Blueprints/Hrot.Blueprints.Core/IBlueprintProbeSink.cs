using Fdp.Core;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Thin probe sink that generated Blueprint code calls into via DebugProbe.
/// </summary>
public interface IBlueprintProbeSink
{
    void OnNodeEnter(Entity self, string nodeId);
    void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged;
    void OnPeerCallEnter(Entity entity, string targetAssetName, string targetGraphName);
    void OnPeerCallExit(Entity entity);
}
