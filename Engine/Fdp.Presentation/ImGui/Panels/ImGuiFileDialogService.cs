using System;
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
    private string _currentDirectory = Directory.GetCurrentDirectory();
    private string _fileNameBuffer   = string.Empty;
    private string _extensionFilter  = "*";
    private TaskCompletionSource<string?>? _tcs;

    /// <inheritdoc/>
    public Task<string?> ShowSaveAsDialogAsync(string defaultFileName, string extensionFilter)
    {
        // Cancel any pending dialog.
        _tcs?.TrySetCanceled();

        _fileNameBuffer   = defaultFileName;
        _extensionFilter  = extensionFilter;
        _currentDirectory = Directory.GetCurrentDirectory();
        _tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _isOpen = true;
        return _tcs.Task;
    }

    /// <summary>
    /// Renders the file dialog for the current frame.
    /// Must be called inside an active ImGui frame (between <c>NewFrame</c> and <c>Render</c>).
    /// </summary>
    public void Draw()
    {
        if (!_isOpen) return;

        bool open = true;
        if (ImGuiApi.BeginPopupModal("Save As##FileDialog", ref open,
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            // Current directory label
            ImGuiApi.Text("Directory: " + _currentDirectory);

            // Up button
            if (ImGuiApi.Button("Up") && Directory.GetParent(_currentDirectory) is { } parent)
                _currentDirectory = parent.FullName;

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
                    if (ImGuiApi.Selectable(fileName, false))
                        _fileNameBuffer = fileName;
                }
            }
            catch (UnauthorizedAccessException) { ImGuiApi.TextDisabled("[Access denied]"); }
            ImGuiApi.EndChild();

            ImGuiApi.Separator();

            // File name input
            byte[] buf = Encoding.UTF8.GetBytes(_fileNameBuffer.PadRight(256, '\0'));
            Array.Resize(ref buf, 256);
            if (ImGuiApi.InputText("File name", buf, (uint)buf.Length))
                _fileNameBuffer = Encoding.UTF8.GetString(buf).TrimEnd('\0');

            if (ImGuiApi.Button("Save"))
            {
                string result = Path.Combine(_currentDirectory, _fileNameBuffer);
                _isOpen = false;
                _tcs?.TrySetResult(result);
                _tcs = null;
                ImGuiApi.CloseCurrentPopup();
            }
            ImGuiApi.SameLine();
            if (ImGuiApi.Button("Cancel"))
            {
                _isOpen = false;
                _tcs?.TrySetResult(null);
                _tcs = null;
                ImGuiApi.CloseCurrentPopup();
            }

            if (!open) // User clicked the X
            {
                _isOpen = false;
                _tcs?.TrySetResult(null);
                _tcs = null;
            }

            ImGuiApi.EndPopup();
        }
        else if (_isOpen)
        {
            // First frame after ShowSaveAsDialogAsync: open the popup.
            ImGuiApi.OpenPopup("Save As##FileDialog");
        }
    }
}
