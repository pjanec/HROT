using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.HotReload;

namespace Hrot.Hsm.Editor.HotReload;

// Classifies an HSM hot-reload tier by comparing StructureHash and ParameterHash
// of the previous and next HsmDefinitionBlob headers.
// Delegates to the shared HotReloadClassifier.
public static class HsmQuickReloadHasher
{
    public static HotReloadTier Classify(HsmDefinitionBlob previous, HsmDefinitionBlob next) =>
        HotReloadClassifier.Classify(
            (int)previous.Header.StructureHash, (int)next.Header.StructureHash,
            (int)previous.Header.ParameterHash, (int)next.Header.ParameterHash);
}
