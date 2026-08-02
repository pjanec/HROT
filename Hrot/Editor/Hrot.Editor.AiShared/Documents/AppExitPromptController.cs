using System;
using System.Collections.Generic;
using System.Linq;

namespace Hrot.Editor.AiShared.Documents;

/// <summary>
/// Headless state machine behind the app-exit "unsaved changes" prompt. Owns the decision of whether
/// the editor may close, and the resolution of the modal's three choices. The ImGui modal
/// (EditorSubsystem) is a thin view over this — it reads <see cref="DirtyDocuments"/> and calls the
/// <c>Resolve*</c> methods on button clicks, so all logic here is unit-testable without an ImGui context.
///
/// <para>
/// Saving is decoupled from closing everywhere else (the per-tab prompt, the removed close-flush and
/// exit Save-All): only an explicit user choice persists. This prompt is the app-wide counterpart — a
/// window-[X] with unsaved documents raises it instead of silently discarding.
/// </para>
/// </summary>
public sealed class AppExitPromptController
{
    private readonly AiDocumentManager _documents;
    private readonly Action            _saveAll;

    /// <param name="documents">The shared document manager (source of dirty state).</param>
    /// <param name="saveAll">
    ///   Persists every dirty document (the composition root's Save-All callback). Invoked by
    ///   <see cref="ResolveSaveAndExit"/>.
    /// </param>
    public AppExitPromptController(AiDocumentManager documents, Action saveAll)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _saveAll   = saveAll   ?? throw new ArgumentNullException(nameof(saveAll));
    }

    /// <summary>True while the unsaved-changes modal should be shown.</summary>
    public bool IsPrompting { get; private set; }

    /// <summary>True once the user has chosen to exit (Save &amp; Exit or Discard &amp; Exit).</summary>
    public bool ExitApproved { get; private set; }

    /// <summary>The open documents with unsaved edits, in open order (drives the modal's list).</summary>
    public IReadOnlyList<AiDocument> DirtyDocuments =>
        _documents.OpenDocuments.Where(d => d.IsDirty).ToList();

    /// <summary>
    /// Called when the user requests app exit (window [X]). Returns <c>true</c> when it is safe to
    /// exit immediately (no unsaved documents, or the user already approved exit); returns <c>false</c>
    /// after opening the prompt because unsaved documents exist. Idempotent while already prompting.
    /// </summary>
    public bool RequestExit()
    {
        if (ExitApproved) return true;            // already resolved in favour of exiting
        if (DirtyDocuments.Count == 0) return true; // nothing to save → close now
        IsPrompting = true;                        // (idempotent: re-setting true is a no-op)
        return false;
    }

    /// <summary>Modal "Save All &amp; Exit": persist every dirty document, then approve exit.</summary>
    public void ResolveSaveAndExit()
    {
        _saveAll();
        ExitApproved = true;
        IsPrompting  = false;
    }

    /// <summary>Modal "Discard &amp; Exit": approve exit without saving.</summary>
    public void ResolveDiscardAndExit()
    {
        ExitApproved = true;
        IsPrompting  = false;
    }

    /// <summary>Modal "Cancel" (also Esc / dismiss): keep the editor open, nothing saved.</summary>
    public void ResolveCancel() => IsPrompting = false;
}
