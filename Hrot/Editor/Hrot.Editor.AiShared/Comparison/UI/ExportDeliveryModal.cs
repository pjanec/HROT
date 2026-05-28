using System.Numerics;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Comparison.UI;

/// <summary>
/// Testable state model for <see cref="ExportDeliveryModal"/>.
/// No ImGui calls; can be used directly in unit tests.
/// See design §4.5.
/// </summary>
public sealed class ExportDeliveryModalState
{
    /// <summary>The maximum export text size that can be placed on the clipboard (8 MB).</summary>
    public const int MaxClipboardBytes = 8 * 1024 * 1024;

    /// <summary>The full export text produced by <see cref="ComparisonExportBuilder"/>.</summary>
    public string ExportText { get; }

    /// <summary>The asset name used in the default save filename.</summary>
    public string AssetName { get; }

    public ExportDeliveryModalState(string exportText, string assetName)
    {
        ExportText = exportText;
        AssetName = assetName;
    }

    /// <summary>
    /// Writes <see cref="ExportText"/> to the given file path as UTF-8.
    /// Returns null on success, or an error message string on failure.
    /// </summary>
    public string? SaveToFile(string filePath)
    {
        try
        {
            File.WriteAllText(filePath, ExportText, System.Text.Encoding.UTF8);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Returns <see cref="ExportText"/> when it fits within the 8 MB clipboard limit,
    /// or null when it exceeds the limit.
    /// </summary>
    public string? GetClipboardText()
        => System.Text.Encoding.UTF8.GetByteCount(ExportText) <= MaxClipboardBytes
            ? ExportText
            : null;

    /// <summary>
    /// Returns a preview of the export text.
    /// When <paramref name="showFull"/> is false, returns the first 30 lines plus a
    /// truncation marker when additional lines exist.
    /// When <paramref name="showFull"/> is true, returns the complete text.
    /// </summary>
    public string GetPreviewText(bool showFull = false)
    {
        if (showFull)
            return ExportText;

        var lines = ExportText.Split('\n');
        var preview = string.Join('\n', lines.Take(30));
        return lines.Length > 30
            ? preview + "\n[...] (Show full to see remaining lines)"
            : preview;
    }

    /// <summary>
    /// Generates the default save filename:
    /// <c>{AssetName}_comparison_{timestamp:yyyyMMdd_HHmmss}.txt</c>
    /// </summary>
    public string GetDefaultFileName()
        => $"{AssetName}_comparison_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt";
}

// ---------------------------------------------------------------------------

/// <summary>
/// ImGui modal that presents the generated export text to the user with options to
/// copy to clipboard, save to file, and preview the content.
/// The state model <see cref="ExportDeliveryModalState"/> is tested separately.
/// See design §4.5.
/// </summary>
public sealed class ExportDeliveryModal
{
    private ExportDeliveryModalState? _state;
    private bool _openPending;
    private bool _showFull;
    private bool _active;
    private string _savePath = "";
    private string? _lastError;
    private bool _copyDisabled;

    /// <summary>Requests the modal to open with the given export result on the next frame.</summary>
    public void Open(string exportText, string assetName)
    {
        _state = new ExportDeliveryModalState(exportText, assetName);
        _openPending = true;
        _showFull = false;
        _lastError = null;
        _copyDisabled = _state.GetClipboardText() == null;
        _savePath = _state.GetDefaultFileName();
    }

    /// <summary>
    /// Renders the modal every frame.
    /// Returns true while the modal is open, false once it has been closed.
    /// </summary>
    public bool Render()
    {
        if (_openPending)
        {
            ImGui.OpenPopup("Comparison Export##deliverymod");
            _openPending = false;
            _active = true;
        }

        if (!_active || _state == null)
            return false;

        var modalOpen = true;

        if (ImGui.BeginPopupModal("Comparison Export##deliverymod", ref modalOpen,
            ImGuiWindowFlags.AlwaysAutoResize))
        {
            var preview = _state.GetPreviewText(_showFull);

            ImGui.InputTextMultiline(
                "##exportpreview",
                ref preview,
                (uint)(preview.Length + 1),
                new Vector2(700, 300),
                ImGuiInputTextFlags.ReadOnly);

            ImGui.Checkbox("Show full export", ref _showFull);
            ImGui.Separator();

            ImGui.Text("Save to file:");
            ImGui.SameLine();
            ImGui.InputText("##savepath", ref _savePath, 2048);
            ImGui.SameLine();
            if (ImGui.Button("Save"))
                _lastError = _state.SaveToFile(_savePath);

            if (_lastError != null)
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), _lastError);

            ImGui.Separator();

            if (_copyDisabled) ImGui.BeginDisabled();
            if (ImGui.Button("Copy to clipboard"))
            {
                var text = _state.GetClipboardText();
                if (text != null)
                    ImGui.SetClipboardText(text);
            }
            if (_copyDisabled)
            {
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Export exceeds 8 MB clipboard limit");
            }

            ImGui.SameLine();
            if (ImGui.Button("Close"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        if (!modalOpen)
            _active = false;

        return _active;
    }
}
