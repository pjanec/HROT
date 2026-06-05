namespace Hrot.Editor.AiShared;

/// <summary>
/// Implemented by editor-owned BTree and HSM assets that support post-reload stitching
/// (design §6.6 / PU-302).  The stitch operation matches editor-model nodes to their
/// corresponding runtime blob nodes in <paramref name="fresh"/> by stable visual IDs
/// (VisualId for BTree, StableId + VisualId for HSM) and copies <c>KernelBlobIndex</c>/
/// <c>FlatIndex</c> across, without replacing the JSON-authoritative topology.
/// </summary>
public interface IStitchableAsset : IEditableAsset
{
    /// <summary>
    /// Stitches runtime indices from the freshly assembly-projected <paramref name="fresh"/>
    /// asset onto this JSON-loaded model.
    /// <para>
    /// <b>Contract:</b> must not call MarkDirty (PU-602).  Must tolerate
    /// <paramref name="fresh"/> being null (compile failure → all indices set to sentinel
    /// and a diagnostic recorded).
    /// </para>
    /// </summary>
    void StitchRuntimeIndices(IEditableAsset? fresh);
}
