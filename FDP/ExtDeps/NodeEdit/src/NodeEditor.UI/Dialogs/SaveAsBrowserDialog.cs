using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using System.Numerics;

namespace NodeEditor.UI.Dialogs;

/// <summary>
/// An existing item shown in a destination folder.
/// </summary>
/// <param name="Name">Display name of the item.</param>
/// <param name="IconKey">Optional icon key resolved via <see cref="IIconProvider"/>.</param>
public sealed record SaveAsContentItem(string Name, string? IconKey);

/// <summary>
/// Result returned when the user confirms or cancels the Save-As browser dialog.
/// </summary>
/// <param name="Confirmed">True if the user confirmed (Save/Overwrite), false if cancelled.</param>
/// <param name="Name">The chosen name.</param>
/// <param name="DestinationPath">The chosen destination folder relative path.</param>
/// <param name="Overwrite">True if the user confirmed an overwrite of an existing item.</param>
public sealed record SaveAsResult(bool Confirmed, string Name, string DestinationPath, bool Overwrite);

/// <summary>
/// Caller-supplied input to open the Save-As browser dialog.
/// Fully data-driven — callbacks supply folder tree, contents, validation, and side effects.
/// </summary>
public sealed class SaveAsRequest
{
    /// <summary>Window title shown to the user.</summary>
    public required string Title { get; init; }

    /// <summary>Initial name for the text field.</summary>
    public string InitialName { get; init; } = "";

    /// <summary>Initially selected folder relative path ("" = root).</summary>
    public string InitialDestination { get; init; } = "";

    /// <summary>Label for the confirm button.</summary>
    public string ConfirmLabel { get; init; } = "Save";

    /// <summary>
    /// Returns the folder tree to display. Called every frame so newly created
    /// folders appear automatically.
    /// </summary>
    public required Func<CategoryNode> GetFolderTree { get; init; }

    /// <summary>
    /// Returns the content items visible in a given destination folder.
    /// Null means contents pane is not shown.
    /// </summary>
    public Func<string, IReadOnlyList<SaveAsContentItem>>? GetFolderContents { get; init; }

    /// <summary>
    /// Called to create a new folder. First arg is the parent folder relative path,
    /// second is the name for the new folder.
    /// </summary>
    public Action<string, string>? OnCreateFolder { get; init; }

    /// <summary>
    /// Called at commit time to check whether the given name already exists at the
    /// given destination. If true, the dialog shows an overwrite confirmation.
    /// </summary>
    public Func<string, string, bool>? NameExists { get; init; }

    /// <summary>
    /// Validates the name. Returns an error string if invalid, null if valid.
    /// </summary>
    public Func<string, string?>? ValidateName { get; init; }
}

/// <summary>
/// A reusable Save-As browser dialog: type a name, browse/choose a destination folder,
/// create folders, and confirm with commit-time overwrite protection.
/// Generic NodeEdit component — no Hrot.*/asset/editor types.
/// </summary>
public sealed class SaveAsBrowserDialog
{
    private static readonly Vector2 IconSize = new(16f, 16f);

    // ── session state ──────────────────────────────────────────────────────

    private bool _isOpen;
    private SaveAsRequest? _request;
    private Action<SaveAsResult>? _onChosen;

    private string _name = "";
    private string _destination = "";
    private bool _focusName;

    private bool _pendingOverwriteConfirm;

    // New-folder popup state
    private string? _newFolderTarget;
    private string _newFolderName = "";
    private bool _focusNewFolderName;

    // ── public API ─────────────────────────────────────────────────────────

    /// <summary>Whether the dialog is currently open.</summary>
    public bool IsOpen => _isOpen;

    /// <summary>
    /// True after <see cref="ConfirmActive"/> detected an existing name
    /// and before <see cref="ConfirmOverwrite"/> or the user cancels the overwrite.
    /// </summary>
    public bool PendingOverwriteConfirm => _pendingOverwriteConfirm;

    /// <summary>Open the dialog with the given request. Cancels any open session.</summary>
    public void Open(SaveAsRequest request, Action<SaveAsResult> onChosen)
    {
        Close();

        _request = request;
        _onChosen = onChosen;
        _name = request.InitialName;
        _destination = request.InitialDestination;
        _focusName = true;
        _pendingOverwriteConfirm = false;
        _newFolderTarget = null;
        _newFolderName = "";
        _focusNewFolderName = false;

        _isOpen = true;
    }

    /// <summary>Close the dialog, firing the callback with <c>Confirmed:false</c>.</summary>
    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _pendingOverwriteConfirm = false;
        _newFolderTarget = null;

        var result = new SaveAsResult(false, _name, _destination, false);
        _onChosen?.Invoke(result);
        _onChosen = null;
        _request = null;
    }

    /// <summary>Headless setter for the name field.</summary>
    public void SetName(string name) => _name = name;

    /// <summary>Headless setter for the destination path.</summary>
    public void SetDestination(string path) => _destination = path;

    /// <summary>
    /// Validate the current name and either fire <c>onChosen</c> (new name) or set
    /// <see cref="PendingOverwriteConfirm"/> (existing name). Does nothing if the
    /// name is invalid.
    /// </summary>
    public SaveAsResult ConfirmActive()
    {
        if (!_isOpen || _request == null)
            return new SaveAsResult(false, _name, _destination, false);

        // Validate name
        var error = _request.ValidateName?.Invoke(_name);
        if (error != null)
            return new SaveAsResult(false, _name, _destination, false);

        // Check if name already exists at destination
        bool exists = _request.NameExists?.Invoke(_name, _destination) ?? false;
        if (exists)
        {
            _pendingOverwriteConfirm = true;
            return new SaveAsResult(true, _name, _destination, false);
        }

        // New name — confirm and close
        var result = new SaveAsResult(true, _name, _destination, false);
        _isOpen = false;
        _onChosen?.Invoke(result);
        _onChosen = null;
        _request = null;
        return result;
    }

    /// <summary>
    /// Fire <c>onChosen</c> with <c>Overwrite:true</c> and close. Only meaningful
    /// when <see cref="PendingOverwriteConfirm"/> is true.
    /// </summary>
    public SaveAsResult ConfirmOverwrite()
    {
        if (!_isOpen || _request == null || !_pendingOverwriteConfirm)
            return new SaveAsResult(false, _name, _destination, false);

        var result = new SaveAsResult(true, _name, _destination, true);
        _isOpen = false;
        _pendingOverwriteConfirm = false;
        _onChosen?.Invoke(result);
        _onChosen = null;
        _request = null;
        return result;
    }

    // ── rendering ──────────────────────────────────────────────────────────

    /// <summary>Render the dialog window. Call once per ImGui frame.</summary>
    public void DrawFrame(IIconProvider icons)
    {
        if (!_isOpen || _request == null) return;

        var size = new Vector2(720f, 560f);
        ImGui.SetNextWindowSize(size, ImGuiCond.Appearing);

        var flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings;

        bool windowOpen = true;
        bool visible = ImGui.Begin(_request.Title + "##saveas_browser", ref windowOpen, flags);

        try
        {
            if (!windowOpen)
            {
                Close();
                return;
            }

            if (!visible) return;

            DrawNameField();
            ImGui.Spacing();
            DrawTwoPanes(icons);
            ImGui.Spacing();
            DrawPathPreview();
            ImGui.Spacing();
            DrawButtons();
        }
        finally
        {
            ImGui.End();
        }

        // Popups rendered outside the main Begin/End so they are not constrained
        // to the child region clipping.
        DrawOverwritePopup();
        DrawNewFolderPopup();
    }

    // ── name field ─────────────────────────────────────────────────────────

    private void DrawNameField()
    {
        if (_focusName)
        {
            ImGui.SetKeyboardFocusHere();
            _focusName = false;
        }

        ImGui.SetNextItemWidth(-1f);
        var inputFlags = ImGuiInputTextFlags.EnterReturnsTrue;
        bool nameEnter = ImGui.InputText("Name", ref _name, 256, inputFlags);

        // Validate name
        string? error = _request?.ValidateName?.Invoke(_name);
        if (error != null)
        {
            ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), error);
        }

        // Enter in name field confirms
        if (nameEnter)
        {
            ConfirmActive();
        }
    }

    // ── two panes ──────────────────────────────────────────────────────────

    private void DrawTwoPanes(IIconProvider icons)
    {
        // Reserve space for path preview + buttons below
        float reservedBelow = ImGui.GetTextLineHeightWithSpacing() * 2
                            + ImGui.GetFrameHeightWithSpacing() * 1.5f;
        float availHeight = Math.Max(ImGui.GetContentRegionAvail().Y - reservedBelow, 80f);

        float leftWidth = ImGui.GetContentRegionAvail().X * 0.4f;

        // Left: Folders
        if (ImGui.BeginChild("##folders_pane", new Vector2(leftWidth, availHeight), ImGuiChildFlags.Borders))
        {
            DrawFolderTree(icons);
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // Right: Contents
        if (ImGui.BeginChild("##contents_pane", new Vector2(0f, availHeight), ImGuiChildFlags.Borders))
        {
            DrawContentsPane(icons);
        }
        ImGui.EndChild();
    }

    // ── folder tree ────────────────────────────────────────────────────────

    private void DrawFolderTree(IIconProvider icons)
    {
        var root = _request?.GetFolderTree();
        if (root == null) return;

        // Render root's children as top-level folders.
        foreach (var child in root.Children)
        {
            DrawFolderNode(child, "", icons);
        }
    }

    private void DrawFolderNode(CategoryNode node, string parentPath, IIconProvider icons)
    {
        string fullPath = string.IsNullOrEmpty(parentPath) ? node.Name : parentPath + "/" + node.Name;
        bool isSelected = fullPath == _destination;

        // Determine folder icon (closed / open) mirroring TreeLayout idiom.
        uint nodeId = ImGui.GetID(fullPath);
        bool wasOpen = ImGui.GetStateStorage().GetInt(nodeId, 0) != 0;
        string folderKey = wasOpen ? "folder_open" : "folder";
        bool hasIcon = icons.TryGet(folderKey, out var folderIcon)
                       || icons.TryGet("folder", out folderIcon);

        if (hasIcon)
        {
            ImGui.Image(folderIcon.TextureId, IconSize, folderIcon.Uv0, folderIcon.Uv1);
            ImGui.SameLine();
        }

        var treeFlags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth;
        if (isSelected)
            treeFlags |= ImGuiTreeNodeFlags.Selected;
        if (node.Children.Count == 0)
            treeFlags |= ImGuiTreeNodeFlags.Leaf;

        bool expanded = ImGui.TreeNodeEx(node.Name, treeFlags);

        // Single-click selects this folder as destination.
        if (ImGui.IsItemClicked())
        {
            _destination = fullPath;
        }

        // Right-click context menu → "New Folder…"
        if (_request?.OnCreateFolder != null && ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("New Folder…"))
            {
                _newFolderTarget = fullPath;
                _newFolderName = "";
                _focusNewFolderName = true;
            }
            ImGui.EndPopup();
        }

        if (expanded)
        {
            foreach (var child in node.Children)
            {
                DrawFolderNode(child, fullPath, icons);
            }
            ImGui.TreePop();
        }
    }

    // ── contents pane ──────────────────────────────────────────────────────

    private void DrawContentsPane(IIconProvider icons)
    {
        string label = string.IsNullOrEmpty(_destination)
            ? "Contents of root"
            : "Contents of " + _destination;
        ImGui.TextDisabled(label);
        ImGui.Separator();

        var contents = _request?.GetFolderContents?.Invoke(_destination);
        if (contents == null || contents.Count == 0)
        {
            ImGui.TextDisabled("(empty)");
            return;
        }

        foreach (var item in contents)
        {
            // Draw type/asset icon if available (mirrors TreeLayout leaf icon idiom).
            if (item.IconKey is { Length: > 0 } key && icons.TryGet(key, out var itemIcon))
            {
                float cursorY = ImGui.GetCursorPosY();
                ImGui.Image(itemIcon.TextureId, IconSize, itemIcon.Uv0, itemIcon.Uv1);
                ImGui.SameLine();
            }
            else
            {
                // Left-pad to align text with icon-bearing items.
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + IconSize.X + 4f);
                ImGui.SameLine();
            }

            // Clicking an item prefills the Name field.
            if (ImGui.Selectable(item.Name + "##" + item.Name))
            {
                _name = item.Name;
            }
        }
    }

    // ── path preview ───────────────────────────────────────────────────────

    private void DrawPathPreview()
    {
        string path = string.IsNullOrEmpty(_destination)
            ? _name
            : _destination + "/" + _name;

        ImGui.TextDisabled("Path: " + path);
    }

    // ── buttons ────────────────────────────────────────────────────────────

    private void DrawButtons()
    {
        string? error = _request?.ValidateName?.Invoke(_name);
        bool isValid = error == null && !string.IsNullOrWhiteSpace(_name);

        // "+ New Folder" button
        if (_request?.OnCreateFolder != null)
        {
            if (ImGui.Button("+ New Folder"))
            {
                _newFolderTarget = _destination;
                _newFolderName = "";
                _focusNewFolderName = true;
            }
            ImGui.SameLine();
        }

        // Confirm button
        ImGui.BeginDisabled(!isValid);
        string confirmLabel = _request?.ConfirmLabel ?? "Save";
        if (ImGui.Button(confirmLabel))
        {
            ConfirmActive();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();

        // Cancel / Esc
        if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            Close();
        }

        // Global Enter in window (when name field doesn't have focus).
        bool globalEnter = ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter);
        if (globalEnter && isValid && !ImGui.IsAnyItemActive())
        {
            ConfirmActive();
        }
    }

    // ── overwrite confirmation popup ───────────────────────────────────────

    private void DrawOverwritePopup()
    {
        if (!_pendingOverwriteConfirm) return;

        const string popupId = "Overwrite?##overwrite_popup";

        if (!ImGui.IsPopupOpen(popupId))
        {
            ImGui.OpenPopup(popupId);
        }

        bool popupOpen = true;
        if (ImGui.BeginPopupModal(popupId, ref popupOpen,
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove))
        {
            string destLabel = _destination.Length > 0 ? _destination : "root";
            ImGui.Text($"'{_name}' already exists in {destLabel}.");
            ImGui.Text("Overwrite?");
            ImGui.Spacing();

            if (ImGui.Button("Overwrite", new Vector2(120, 0)))
            {
                ConfirmOverwrite();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(120, 0))
                || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _pendingOverwriteConfirm = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        // If popup was closed externally (X button or lost focus),
        // reset the pending state.
        if (!popupOpen)
        {
            _pendingOverwriteConfirm = false;
        }
    }

    // ── new folder popup ───────────────────────────────────────────────────

    private void DrawNewFolderPopup()
    {
        if (_newFolderTarget == null) return;

        const string popupId = "New Folder##new_folder_popup";

        if (_focusNewFolderName)
        {
            ImGui.OpenPopup(popupId);
            _focusNewFolderName = false;
        }

        bool popupOpen = true;
        if (ImGui.BeginPopupModal(popupId, ref popupOpen,
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove))
        {
            ImGui.Text($"Create folder in '{_newFolderTarget}'");
            ImGui.Spacing();

            // Auto-focus the name field.
            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();

            bool nameEnter = ImGui.InputText("##new_folder_name", ref _newFolderName, 128,
                ImGuiInputTextFlags.EnterReturnsTrue);

            bool canCreate = !string.IsNullOrWhiteSpace(_newFolderName);

            ImGui.BeginDisabled(!canCreate);
            if (ImGui.Button("Create", new Vector2(120, 0)) || (nameEnter && canCreate))
            {
                string trimmedName = _newFolderName.Trim();
                _request?.OnCreateFolder?.Invoke(_newFolderTarget, trimmedName);

                // Select the newly created folder.
                string newFolderPath = _newFolderTarget.Length > 0
                    ? _newFolderTarget + "/" + trimmedName
                    : trimmedName;
                _destination = newFolderPath;
                _newFolderTarget = null;
                _newFolderName = "";
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(120, 0))
                || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _newFolderTarget = null;
                _newFolderName = "";
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        // If popup was closed externally, reset.
        if (!popupOpen)
        {
            _newFolderTarget = null;
            _newFolderName = "";
        }
    }
}
