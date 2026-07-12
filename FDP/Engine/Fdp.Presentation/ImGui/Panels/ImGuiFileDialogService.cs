using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Fdp.Presentation.Abstractions;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Panels;

/// <summary>
/// ImGui-based implementation of <see cref="IFileDialogService"/>.
///
/// <para>
/// Renders a modal "Save As" popup each frame via <see cref="Draw"/>.
/// The popup resolves asynchronously: the returned <see cref="Task{T}"/> completes
/// when the user clicks Save or Cancel (or clicks the X button).
/// </para>
///
/// <para>
/// Only one dialog is active at a time.  Calling <see cref="ShowSaveAsDialogAsync"/>
/// while a previous dialog is pending cancels the previous task.
/// </para>
/// </summary>
public sealed class ImGuiFileDialogService : IFileDialogService
{
    private bool   _isOpen;
    private bool   _multiSelect;
    private string _currentDirectory = Directory.GetCurrentDirectory();
    private string _fileNameBuffer   = string.Empty;
    private string _extensionFilter  = "*";
    private string _dialogTitle      = "Save As";
    private TaskCompletionSource<string?>? _tcs;
    private TaskCompletionSource<string[]?>? _multiTcs;

    // Multi-select state: full paths of files checked by the user. Persists across
    // directory navigation within a single multi-select dialog session.
    private readonly HashSet<string> _selectedFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<string?> ShowSaveAsDialogAsync(string callSiteId, string defaultFileName, string extensionFilter)
        => OpenDialogInternal("Save As", defaultFileName, extensionFilter);

    /// <inheritdoc/>
    public Task<string?> ShowOpenFileDialogAsync(string callSiteId, string extensionFilter)
        => OpenDialogInternal("Open File", string.Empty, extensionFilter);

    /// <inheritdoc/>
    public Task<string[]?> ShowOpenMultipleFilesDialogAsync(string callSiteId, string extensionFilter)
        => OpenMultiDialogInternal("Open Files", extensionFilter);

    private Task<string?> OpenDialogInternal(string title, string defaultFileName, string extensionFilter)
    {
        // Cancel any pending dialog (single- or multi-select).
        _tcs?.TrySetCanceled();
        _multiTcs?.TrySetCanceled();

        _fileNameBuffer   = defaultFileName;
        _extensionFilter  = extensionFilter;
        _dialogTitle      = title;
        _multiSelect      = false;
        if (!Directory.Exists(_currentDirectory))
            _currentDirectory = Directory.GetCurrentDirectory();
        _tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _multiTcs = null;
        _isOpen = true;
        return _tcs.Task;
    }

    private Task<string[]?> OpenMultiDialogInternal(string title, string extensionFilter)
    {
        // Cancel any pending dialog (single- or multi-select).
        _tcs?.TrySetCanceled();
        _multiTcs?.TrySetCanceled();

        _fileNameBuffer   = string.Empty;
        _extensionFilter  = extensionFilter;
        _dialogTitle      = title;
        _multiSelect      = true;
        _selectedFiles.Clear();
        if (!Directory.Exists(_currentDirectory))
            _currentDirectory = Directory.GetCurrentDirectory();
        _multiTcs = new TaskCompletionSource<string[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tcs = null;
        _isOpen = true;
        return _multiTcs.Task;
    }

    /// <summary>
    /// Renders the file dialog for the current frame.
    /// Must be called inside an active ImGui frame (between <c>NewFrame</c> and <c>Render</c>).
    /// </summary>
    public void Draw()
    {
        if (!_isOpen) return;

        bool open = true;
        if (ImGuiApi.BeginPopupModal($"{_dialogTitle}##FileDialog", ref open,
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            // Current directory label
            ImGuiApi.Text("Directory: " + _currentDirectory);

            // Up button
            if (ImGuiApi.Button("Up") && Directory.GetParent(_currentDirectory) is { } parent)
                _currentDirectory = parent.FullName;

            ImGuiApi.SameLine();
            // Home shortcut: useful on non-Windows where the drive combo only ever shows "/".
            if (ImGuiApi.Button("Home"))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home) && Directory.Exists(home))
                    _currentDirectory = home;
            }

            ImGuiApi.SameLine();
            ImGuiApi.SetNextItemWidth(80f);
            string? currentRoot = Path.GetPathRoot(_currentDirectory);
            if (ImGuiApi.BeginCombo("##drives", string.IsNullOrEmpty(currentRoot) ? "<drive>" : currentRoot))
            {
                foreach (string drive in Directory.GetLogicalDrives())
                {
                    if (ImGuiApi.Selectable(drive))
                        _currentDirectory = drive;
                }
                ImGuiApi.EndCombo();
            }

            ImGuiApi.Separator();

            // List dirs and matching files
            ImGuiApi.BeginChild("##filelist", new Vector2(400, 200));
            try
            {
                foreach (string dir in Directory.GetDirectories(_currentDirectory))
                {
                    string dirName = Path.GetFileName(dir);
                    if (ImGuiApi.Selectable("[DIR] " + dirName, false, ImGuiSelectableFlags.AllowDoubleClick))
                        if (ImGuiApi.IsMouseDoubleClicked(0))
                            _currentDirectory = dir;
                }
                foreach (string file in Directory.GetFiles(_currentDirectory, _extensionFilter))
                {
                    string fileName = Path.GetFileName(file);
                    if (_multiSelect)
                    {
                        // Multi-select mode: one checkbox per file, keyed by full path so
                        // selections survive navigating between directories.
                        bool isChecked = _selectedFiles.Contains(file);
                        if (ImGuiApi.Checkbox(fileName, ref isChecked))
                        {
                            if (isChecked)
                                _selectedFiles.Add(file);
                            else
                                _selectedFiles.Remove(file);
                        }
                    }
                    else
                    {
                        if (ImGuiApi.Selectable(fileName, false))
                            _fileNameBuffer = fileName;
                    }
                }
            }
            catch (UnauthorizedAccessException) { ImGuiApi.TextDisabled("[Access denied]"); }
            ImGuiApi.EndChild();

            ImGuiApi.Separator();

            if (_multiSelect)
            {
                // Multi-select mode: no free-text file name — selection is via the checkboxes above.
                ImGuiApi.Text($"Selected: {_selectedFiles.Count} file(s)");
            }
            else
            {
                // File name input
                byte[] buf = Encoding.UTF8.GetBytes(_fileNameBuffer.PadRight(256, '\0'));
                Array.Resize(ref buf, 256);
                if (ImGuiApi.InputText("File name", buf, (uint)buf.Length))
                    _fileNameBuffer = Encoding.UTF8.GetString(buf).TrimEnd('\0');
            }

            string confirmLabel = _multiSelect ? "Open" : (_dialogTitle == "Open File" ? "Open" : "Save");
            if (ImGuiApi.Button(confirmLabel))
            {
                _isOpen = false;
                if (_multiSelect)
                {
                    string[] result = new string[_selectedFiles.Count];
                    _selectedFiles.CopyTo(result);
                    _multiTcs?.TrySetResult(result);
                    _multiTcs = null;
                }
                else
                {
                    string result = Path.Combine(_currentDirectory, _fileNameBuffer);
                    _tcs?.TrySetResult(result);
                    _tcs = null;
                }
                ImGuiApi.CloseCurrentPopup();
            }
            ImGuiApi.SameLine();
            if (ImGuiApi.Button("Cancel"))
            {
                _isOpen = false;
                if (_multiSelect)
                {
                    _multiTcs?.TrySetResult(null);
                    _multiTcs = null;
                }
                else
                {
                    _tcs?.TrySetResult(null);
                    _tcs = null;
                }
                ImGuiApi.CloseCurrentPopup();
            }

            if (!open) // User clicked the X
            {
                _isOpen = false;
                if (_multiSelect)
                {
                    _multiTcs?.TrySetResult(null);
                    _multiTcs = null;
                }
                else
                {
                    _tcs?.TrySetResult(null);
                    _tcs = null;
                }
            }

            ImGuiApi.EndPopup();
        }
        else if (_isOpen)
        {
            // First frame after ShowSaveAsDialogAsync: open the popup.
            ImGuiApi.OpenPopup($"{_dialogTitle}##FileDialog");
        }
    }
}
