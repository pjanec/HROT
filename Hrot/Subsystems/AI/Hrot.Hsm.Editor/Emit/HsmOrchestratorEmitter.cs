using System.IO;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.Editor.AiShared.Emit;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;

namespace Hrot.Hsm.Editor.Emit;

/// <summary>
/// Emits a companion <c>{AssetName}.Orchestrators.g.cs</c> file for an HSM master asset that
/// has aliased sub-tree (BTree) variable bindings.
/// One <c>[HsmAction]</c> static method is generated per unique (variable, sub-tree) pair.
/// Returns <c>null</c> when the asset has no alias bindings — the caller should skip writing.
///
/// <para>⭐⭐⭐ <b>Batch 92 (<c>92c</c>) — this is now a THIN CALLER.</b> The emit body lives in
/// <see cref="HsmOrchestratorEmitCore"/>, in the netstandard2.0 persistence assembly, so the
/// generator (<c>92b</c>) and this sidecar path emit from <b>one body</b> — 📌 ruling 9.</para>
///
/// <para>⭐⭐ <b><c>WriteOrchestratorFile</c> STAYS.</b> It serves the <b>Category-1</b> hand-authored
/// path that <c>EditorSubsystem:3136</c> keeps per spec. ⛔ It is deliberately not wired to anything
/// new here — <c>Q45</c> ruled against that.</para>
/// </summary>
public static class HsmOrchestratorEmitter
{
    /// <summary>
    /// Generates the orchestrator source text for <paramref name="asset"/>.
    /// Returns <c>null</c> when there are no alias bindings.
    /// </summary>
    /// <remarks>
    /// ⭐ Projects through the SAME <c>ToDto</c> the save path uses — ⛔ not a second projection —
    /// so the editor sidecar and the generated file cannot diverge in what they read.
    /// </remarks>
    public static string? Emit(HsmAsset asset)
        => HsmOrchestratorEmitCore.Emit(HsmAssetMapper.ToDto(asset));

    /// <summary>
    /// Writes the sidecar file using atomic write. No-op when <paramref name="sidecarContent"/> is
    /// <c>null</c> (no aliases; existing file is preserved).
    /// </summary>
    public static void WriteOrchestratorFile(HsmAsset asset, string? sidecarContent)
    {
        if (sidecarContent is null) return;
        string path = Path.ChangeExtension(asset.SourceFilePath, null) + ".Orchestrators.g.cs";
        FluentCSharpEmitterBase.WriteAtomic(path, sidecarContent);
    }
}
