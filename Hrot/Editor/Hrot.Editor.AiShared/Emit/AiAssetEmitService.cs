using System;
using Hrot.Editor.AiShared;

namespace Hrot.Editor.AiShared.Emit;

/// <summary>
/// Emits deterministic C# for an AI asset and writes it atomically to the
/// asset's <see cref="IEditableAsset.SourceFilePath"/>.
///
/// <para>
/// This is a thin façade used by <see cref="RegenerationScheduler"/> as the
/// <c>flushAction</c>.  The actual code generation is delegated to the kind-specific
/// emitters (<c>BTreeFluentEmitter</c> / <c>HsmFluentEmitter</c>) which are injected
/// as typed emit delegates so the service stays free of direct assembly references
/// to the BTree/HSM editor assemblies.
/// </para>
///
/// <para>
/// <b>Atomic write:</b> <see cref="FluentCSharpEmitterBase.WriteAtomic"/> writes to a
/// <c>.tmp</c> sidecar then moves it over the target, and skips the write entirely
/// when the generated content is byte-identical to the existing file.
/// </para>
/// </summary>
public sealed class AiAssetEmitService
{
    /// <summary>
    /// Delegate called with the asset to emit and the generated C# content.
    /// Responsible for any post-emit cleanup (e.g. clearing the in-memory dirty flag).
    /// </summary>
    public delegate void PostEmitAction(IEditableAsset asset, bool fileWasWritten);

    private readonly Func<IEditableAsset, string?> _emitDelegate;
    private readonly PostEmitAction? _postEmit;

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <param name="emitDelegate">
    ///   Called with the dirty asset; returns the generated C# source text, or
    ///   <c>null</c> if the asset kind is not supported.
    /// </param>
    /// <param name="postEmit">
    ///   Optional callback invoked after each atomic write.  Receives the asset and
    ///   whether the file was actually written (<c>false</c> = byte-identical, no-op).
    ///   Use this to clear the in-memory dirty flag.
    /// </param>
    public AiAssetEmitService(
        Func<IEditableAsset, string?> emitDelegate,
        PostEmitAction? postEmit = null)
    {
        _emitDelegate = emitDelegate ?? throw new ArgumentNullException(nameof(emitDelegate));
        _postEmit     = postEmit;
    }

    /// <summary>
    /// Emits C# for <paramref name="asset"/> and writes it atomically.
    /// </summary>
    /// <param name="asset">The dirty asset to emit.</param>
    /// <returns>
    ///   <c>true</c> when the file was written (content changed or new),
    ///   <c>false</c> when byte-identical content was already on disk or the asset
    ///   has no valid <see cref="IEditableAsset.SourceFilePath"/>.
    /// </returns>
    public bool Emit(IEditableAsset asset)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));

        var filePath = asset.SourceFilePath;
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var content = _emitDelegate(asset);
        if (content is null)
            return false;

        bool written = FluentCSharpEmitterBase.WriteAtomic(filePath, content);
        _postEmit?.Invoke(asset, written);
        return written;
    }
}
