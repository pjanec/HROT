using System;
using Hrot.Editor.AiShared.Refactor;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// ⭐⭐⭐ <b>"Rename…" on an ASSET — the prompt, and the PREVIEW it feeds.</b>
/// 📄 <c>AI_Editor_Shared_Infrastructure.md</c> §16.1, operation <b>4</b>: <i>"<b>Rename asset</b> —
/// change the asset's class name, file name, and <c>Name</c>/<c>AssetId</c> attribute argument; update
/// every reference (subtree calls, composite-actor references, BlueprintCall nodes, AiPrimitive name
/// lookups)."</i>
///
/// <para>⭐⭐ <b>EXTRACTED from <c>InspectorWindow</c>, not rewritten.</b> 📌 §7.4's rule for the whole
/// programme — the buffer, the modal, the <c>PreviewRename</c> call and the hand-off to
/// <c>FindResultsWindow</c> are the retired header's, moved. ⛔ The Inspector's copy is DELETED in the
/// same commit; there is no second implementation.</para>
///
/// <para>🔒 <b>Why it moved (user, <c>2026-08-22</c>):</b> <i>"go to definition and rename and find
/// references, these all sound like context menu items … asset related context menu items then, still
/// nothing for a details panel view."</i> ⇒ ⭐ its entry point is now the Asset Browser's row menu.</para>
///
/// <para>⭐⭐⭐ <b>IT PREVIEWS, IT NEVER APPLIES.</b> §16.2 splits <c>PreviewRename</c> from
/// <c>ApplyRename</c> deliberately — <i>"The preview lists every file that would be modified … The user
/// reviews and either applies or cancels."</i> ⇒ ⛔ this dialog's OK produces a <b>preview</b> that lands
/// in the Find Results window; ⚠ applying is that window's gesture, not this one's. The retired header
/// behaved the same way, and this keeps it.</para>
///
/// <para>⚠ <b>Drawn as a FRAME OVERLAY</b>, never from a window's client area — 📌 <c>BP-327</c>, three
/// occurrences: <c>ManagedWindow.Render</c> returns early when the window is closed or belongs to
/// another perspective, and a modal drawn there vanishes with the panel it was opened from.</para>
/// </summary>
public sealed class AssetRenameModal
{
    /// <summary>⭐ Per-INSTANCE popup id — 📌 <c>VariableCreateModal</c>'s lesson: two instances sharing
    /// one popup id is ONE window both append into, and its first button belongs to the other one.</summary>
    private const string PopupId = "Rename Asset##ai_asset_rename";

    /// <summary>⭐ Headless seam — <c>ModalPopupIdTests</c> asserts the ids are pairwise distinct.</summary>
    internal static string PopupIdForTest => PopupId;

    private readonly IRefactorService _refactorService;
    private readonly Action<RefactorPreview> _showPreview;

    private readonly byte[] _buffer = new byte[512];
    private string?          _pendingKey;
    private bool             _openRequested;

    /// <param name="refactorService">
    /// ⭐⭐ <b>REQUIRED.</b> 📌 The silent-default ruling — a rename that skips the refactor service
    /// dangles every reference *(<c>M-15</c>)*, so there is no <c>null</c> arm to fall into.
    /// </param>
    /// <param name="showPreview">
    /// ⭐ Where the computed preview goes — the Find Results window. ⛔ Injected because that window is
    /// the composition root's, and this type must not learn which one.
    /// </param>
    public AssetRenameModal(IRefactorService refactorService, Action<RefactorPreview> showPreview)
    {
        _refactorService = refactorService ?? throw new ArgumentNullException(nameof(refactorService));
        _showPreview     = showPreview     ?? throw new ArgumentNullException(nameof(showPreview));
    }

    /// <summary>⭐ True while the dialog is open on some asset. A rail surface — ⛔ no ImGui needed.</summary>
    public bool IsOpen => _pendingKey is not null;

    /// <summary>⭐ The asset name the dialog is open on, or <see langword="null"/>.</summary>
    public string? PendingKey => _pendingKey;

    /// <summary>
    /// ⭐⭐ Open the dialog on <paramref name="assetName"/>. ⛔ Refuses a blank name rather than opening a
    /// dialog that could only rename <c>""</c> — ⚠ and says so by returning <c>false</c>.
    /// </summary>
    public bool Open(string? assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName)) return false;

        _pendingKey    = assetName;
        _openRequested = true;
        Array.Clear(_buffer, 0, _buffer.Length);
        return true;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Compute the preview and hand it over.</b> ⛔ Separated from the draw so the CONSEQUENCE
    /// is railable without ImGui — 📌 <c>R-21</c>/<c>R-62</c>: the draw is unrailed by construction.
    /// <para>⚠ A blank new name is a no-op that still CLOSES, exactly as the retired header behaved —
    /// ⛔ it does not preview a rename to nothing.</para>
    /// </summary>
    public bool Commit(string newName)
    {
        var from = _pendingKey;
        Close();

        if (from is null || string.IsNullOrWhiteSpace(newName)) return false;

        _showPreview(_refactorService.PreviewRename(from, newName, new RefactorOptions()));
        return true;
    }

    /// <summary>⭐ Cancel, or the <c>[x]</c>. ⚠ 📌 <c>B100c</c>: closing ENDS the session — it does not
    /// leave a half-open state a later frame re-reads.</summary>
    public void Close()
    {
        _pendingKey = null;
        Array.Clear(_buffer, 0, _buffer.Length);
    }

    /// <summary>⭐ Submit the popup. ⚠ Safe to call every frame; draws nothing when closed.</summary>
    public void Draw()
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;

        if (_openRequested)
        {
            ImGui.OpenPopup(PopupId);
            _openRequested = false;
        }

        if (_pendingKey is null) return;

        // ⭐ B100b: give the content real room, or the name box collapses to the clamp floor.
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(460, 0), ImGuiCond.Appearing);

        bool open = true;
        if (ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted($"Rename asset: {_pendingKey}");
            ImGui.TextDisabled("Every reference across BTree, HSM and Blueprint assets is updated.");
            ImGui.Separator();

            ImGui.TextUnformatted("New name:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##ai_asset_rename_name", _buffer, (uint)_buffer.Length);

            ImGui.Separator();
            if (ImGui.Button("Preview…", new System.Numerics.Vector2(110, 0)))
            {
                Commit(Fdp.Presentation.Utils.ImGuiBufferText.Decode(_buffer));
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new System.Numerics.Vector2(110, 0)))
            {
                Close();
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        // ⚠ The [x] closes the popup without running either button — 📌 B100c: end the session.
        if (!open) Close();
    }
}
