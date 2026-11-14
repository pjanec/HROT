using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hrot.UI.Common.RunMode;

namespace Hrot.Editor.AiShared.Documents;

/// <summary>
/// Headless state machine behind the app-exit "unsaved changes" prompt. Owns the decision of whether
/// the editor may close, and the resolution of the modal's three choices.
/// ⭐⭐ <c>CE-1438</c> (R-3066 audit #5) — the prompt is a <see cref="RunModeConfirmation"/> (<see cref="Pending"/>: its text lists the documents, its buttons
/// are Save All &amp; Exit / Discard &amp; Exit / Cancel), drawn by the ONE <see cref="ConfirmationModal"/>; ⛔ this type keeps no button list of its own.
/// The <c>Resolve*</c> methods stay for a caller that decides without the modal, so all logic here is unit-testable without an ImGui context.
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

    /// <summary>⭐ The unsaved-changes confirmation (draw it with <see cref="ConfirmationModal.Draw"/>); <see langword="null"/> when not prompting.</summary>
    public RunModeConfirmation? Pending { get; private set; }

    /// <summary>True while the unsaved-changes modal should be shown.</summary>
    public bool IsPrompting => Pending != null;

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
        Pending ??= BuildPrompt();                 // (idempotent: a second [X] press keeps the one prompt)
        return false;
    }

    private RunModeConfirmation BuildPrompt()
    {
        var dirty = DirtyDocuments;
        var text = new StringBuilder($"You have {dirty.Count} document{(dirty.Count == 1 ? "" : "s")} with unsaved changes:\n");
        foreach (var d in dirty) text.Append("  • ").Append(d.Asset.Name).Append("  (").Append(d.Kind).Append(")\n");
        text.Append("\nSave them before exiting?");
        return RunModeConfirmation.WithCancel(text.ToString(), done => { if (ReferenceEquals(Pending, done)) Pending = null; },
                                              "Unsaved Changes", "ai_app_exit_confirm",
                                              new RunModeConfirmation.Button("Save All & Exit", ResolveSaveAndExit),
                                              new RunModeConfirmation.Button("Discard & Exit", ResolveDiscardAndExit));
    }

    /// <summary>Modal "Save All &amp; Exit": persist every dirty document, then approve exit.</summary>
    public void ResolveSaveAndExit()
    {
        _saveAll();
        ExitApproved = true;
        Pending      = null;
    }

    /// <summary>Modal "Discard &amp; Exit": approve exit without saving.</summary>
    public void ResolveDiscardAndExit()
    {
        ExitApproved = true;
        Pending      = null;
    }

    /// <summary>Modal "Cancel" (also Esc / dismiss): keep the editor open, nothing saved.</summary>
    public void ResolveCancel() => Pending = null;
}
