using Fbt;
using Hrot.Editor.AiShared.HotReload;

namespace Hrot.BTree.Editor.HotReload;

// Classifies a BTree hot-reload tier by comparing StructureHash and ParamHash
// of the previous and next BehaviorTreeBlob.
// Delegates to the shared HotReloadClassifier.
public static class BTreeQuickReloadHasher
{
    public static HotReloadTier Classify(BehaviorTreeBlob previous, BehaviorTreeBlob next) =>
        HotReloadClassifier.Classify(
            previous.StructureHash, next.StructureHash,
            previous.ParamHash,     next.ParamHash);
}
