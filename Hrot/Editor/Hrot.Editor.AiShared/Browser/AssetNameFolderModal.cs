using System.Numerics;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Recipes;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// A generic ImGui modal that renders any <see cref="INameFolderDialog"/> —
/// a <b>Name</b> textbox + <b>folder tree</b> (select existing logical subfolder) +
/// <b>"＋ New subfolder"</b> (create logical subfolder) + Create/Cancel.
/// Used by both New-Asset and Save-As.
/// </summary>
/// <remarks>
/// <para>
/// <b>Design (DBT-A3 / DBT-2):</b> open/close/confirm logic is separated from
/// ImGui draw so the modal is testable headlessly.  Call <see cref="ConfirmActive"/>
/// to simulate user confirmation without an ImGui context.
/// </para>
/// <para>
/// <b>BATCH-26 lock-up fix mirrored from <see cref="AssetPickerModal"/>:</b>
/// while <see cref="IsOpen"/>, <see cref="DrawModal"/> retries
/// <see cref="ImGui.OpenPopup"/> until the popup is actually open, using the
/// <b>identical</b> ID string as <see cref="ImGui.BeginPopupModal"/>, plus an explicit
/// <see cref="ImGui.SetNextWindowSize"/> so the modal can't collapse to zero/invisible size.
/// </para>
/// </remarks>
public sealed class AssetNameFolderModal
{
    /// <summary>
    /// The popup ID string used for both <see cref="ImGui.OpenPopup"/> and
    /// <see cref="ImGui.BeginPopupModal"/>.  Must be identical — see BATCH-26
    /// lock-up diagnosis.
    /// </summary>
    public const string PopupId = "Asset Name And Folder";

    /// <summary>
    /// Default modal window size (BATCH-26: explicit size prevents zero-size collapse).
    /// </summary>
    public static readonly Vector2 DefaultWindowSize = new(560f, 560f);

    private INameFolderDialog? _dialog;
    private Action<IEditableAsset>? _onCreated;
    private string _errorMessage = "";

    // New subfolder input state (per-session, cleared on close).
    private string _newSubfolderName = "";

    // ── Public API ─────────────────────────────────────────────────────

    /// <summary>
    /// Whether the modal is currently open.
    /// </summary>
    public bool IsOpen => _dialog != null;

    /// <summary>
    /// The active dialog, or <see langword="null"/> when the modal is closed.
    /// Exposed for tests that need to verify or manipulate dialog state.
    /// </summary>
    internal INameFolderDialog? Dialog => _dialog;

    /// <summary>
    /// The current onCreated callback, or <see langword="null"/> when no session is active.
    /// Exposed for tests that verify the callback identity.
    /// </summary>
    internal Action<IEditableAsset>? OnCreatedCallback => _onCreated;

    /// <summary>
    /// The current error message shown in the modal, or <see langword="null"/> when
    /// the modal is closed.
    /// </summary>
    internal string ErrorMessage => _errorMessage;

    /// <summary>
    /// Opens the modal over <paramref name="dialog"/>.
    /// <paramref name="onCreated"/> is forwarded to <see cref="INameFolderDialog.Confirm"/>
    /// on a successful Create/Save.
    /// </summary>
    public void Open(INameFolderDialog dialog, Action<IEditableAsset>? onCreated = null)
    {
        if (dialog == null) throw new ArgumentNullException(nameof(dialog));

        // Dispose previous dialog if re-opening while already open.
        CloseInternal();

        _dialog = dialog;
        _onCreated = onCreated;
        _errorMessage = "";
        _newSubfolderName = "";
    }

    /// <summary>
    /// Programmatic close — discards the dialog without calling Confirm.
    /// </summary>
    public void Close()
    {
        CloseInternal();
    }

    // ── Headless test seam ────────────────────────────────────────────

    /// <summary>
    /// Confirms the active dialog (CanConfirm-gated) exactly as the OK button does.
    /// Returns the <see cref="ConfirmResult"/> (or a Fail when not open / cannot confirm).
    /// Closes the modal on success.
    /// </summary>
    public ConfirmResult ConfirmActive()
    {
        if (_dialog == null)
            return ConfirmResult.Fail("No active dialog.");

        if (!_dialog.CanConfirm())
            return ConfirmResult.Fail("Dialog cannot be confirmed (check Name, Recipe/Kind, and service).");

        var result = _dialog.Confirm(_onCreated);

        if (result.IsSuccess)
        {
            CloseInternal();
        }
        else
        {
            _errorMessage = result.Error ?? "Unknown error.";
        }

        return result;
    }

    // ── ImGui draw ─────────────────────────────────────────────────────

    /// <summary>
    /// Per-frame draw. No-op when closed. (BATCH-26 pattern: pending-open flag +
    /// identical PopupId + explicit SetNextWindowSize so the modal is reliably visible.)
    /// </summary>
    public void DrawModal()
    {
        if (!IsOpen)
            return;

        // Open the popup using the EXACT SAME string for OpenPopup/IsPopupOpen/BeginPopupModal
        // (mirrors AssetPickerModal BATCH-26 lock-up fix).
        if (!ImGui.IsPopupOpen(PopupId))
            ImGui.OpenPopup(PopupId);
        ImGui.SetNextWindowSize(DefaultWindowSize, ImGuiCond.Appearing);

        bool isOpen = true;
        if (ImGui.BeginPopupModal(PopupId, ref isOpen, ImGuiWindowFlags.NoDocking))
        {
            var dlg = _dialog!;

            // ── Title ────────────────────────────────────────────────
            ImGui.TextUnformatted(dlg.Title);
            ImGui.Separator();
            ImGui.Spacing();

            // ── Name ─────────────────────────────────────────────────
            var name = dlg.Name ?? "";
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("Name", ref name, 256))
            {
                dlg.Name = name;
                _errorMessage = "";
            }

            ImGui.Spacing();

            // ── Folder tree ───────────────────────────────────────────
            ImGui.TextUnformatted("Folder:");
            ImGui.SameLine();
            var selRelPath = dlg.FolderPicker.SelectedRelPath;
            var displayPath = string.IsNullOrEmpty(selRelPath) ? "(root)" : selRelPath;
            ImGui.TextDisabled(displayPath);

            ImGui.Spacing();

            // Build the tree from known folder paths.
            var root = FolderTreePicker.Build(dlg.FolderPicker.FolderPaths);

            // Render tree in a child area.
            var avail = ImGui.GetContentRegionAvail();
            var treeHeight = MathF.Max(avail.Y - 90f, 100f);
            if (ImGui.BeginChild("##FolderTree", new Vector2(-1f, treeHeight), ImGuiChildFlags.Borders))
            {
                DrawFolderTreeNodes(root, dlg);
                ImGui.EndChild();
            }

            ImGui.Spacing();

            // ── "＋ New subfolder" ────────────────────────────────────
            ImGui.SetNextItemWidth(200f);
            ImGui.InputTextWithHint("##NewSubfolderName", "New subfolder name…", ref _newSubfolderName, 128);
            ImGui.SameLine();

            bool canAdd = !string.IsNullOrWhiteSpace(_newSubfolderName)
                          && dlg.FolderPicker.ContainsFolder(dlg.FolderPicker.SelectedRelPath);

            if (!canAdd)
                ImGui.BeginDisabled();
            if (ImGui.Button("＋ New subfolder") && canAdd)
            {
                try
                {
                    var parent = dlg.FolderPicker.SelectedRelPath;
                    if (!dlg.FolderPicker.ContainsFolder(parent) && !string.IsNullOrEmpty(parent))
                    {
                        _errorMessage = $"Parent folder '{parent}' is not a known folder.";
                    }
                    else
                    {
                        dlg.FolderPicker.AddFolder(parent, _newSubfolderName);
                        _newSubfolderName = "";
                        _errorMessage = "";
                    }
                }
                catch (ArgumentException ex)
                {
                    _errorMessage = ex.Message;
                }
            }
            if (!canAdd)
                ImGui.EndDisabled();

            ImGui.Spacing();

            // ── Error message ─────────────────────────────────────────
            if (!string.IsNullOrEmpty(_errorMessage))
            {
                var errorColor = new Vector4(1f, 0.4f, 0.4f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Text, errorColor);
                ImGui.TextWrapped(_errorMessage);
                ImGui.PopStyleColor();
            }

            ImGui.Spacing();

            // ── Buttons ───────────────────────────────────────────────
            bool isNewDialog = dlg is NewAssetDialog;
            string okLabel = isNewDialog ? "Create" : "Save";
            bool canConfirm = dlg.CanConfirm();

            if (!canConfirm)
                ImGui.BeginDisabled();
            if (ImGui.Button(okLabel) && canConfirm)
            {
                _errorMessage = "";
                var result = dlg.Confirm(_onCreated);
                if (result.IsSuccess)
                {
                    ImGui.CloseCurrentPopup();
                    CloseInternal();
                }
                else
                {
                    _errorMessage = result.Error ?? "Unknown error.";
                }
            }
            if (!canConfirm)
                ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                ImGui.CloseCurrentPopup();
                CloseInternal();
            }

            // Close on X button.
            if (!isOpen)
            {
                ImGui.CloseCurrentPopup();
                CloseInternal();
            }

            ImGui.EndPopup();
        }
    }

    // ── Internals ──────────────────────────────────────────────────────

    private void CloseInternal()
    {
        _dialog = null;
        _onCreated = null;
        _errorMessage = "";
        _newSubfolderName = "";
    }

    /// <summary>
    /// Recursively draws a <see cref="FolderTreeNode"/> tree, mirroring
    /// <see cref="AssetBrowserPanel.DrawTreeNode"/> but for folder selection
    /// (no leaf rendering — only folder nodes are clickable for selection).
    /// </summary>
    private void DrawFolderTreeNodes(FolderTreeNode node, INameFolderDialog dlg)
    {
        foreach (var child in node.Children)
        {
            if (child.IsLeaf)
                continue; // Only folders in the picker.

            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow
                                       | ImGuiTreeNodeFlags.OpenOnDoubleClick;

            string label = string.IsNullOrEmpty(child.FullPath)? "(root)" : child.Name;
            bool isOpen = ImGui.TreeNodeEx(label, flags);

            // Click on the node label → select this folder.
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                dlg.FolderPicker.SelectedRelPath = child.FullPath;
                _errorMessage = "";
            }

            if (isOpen)
            {
                DrawFolderTreeNodes(child, dlg);
                ImGui.TreePop();
            }
        }
    }
}
