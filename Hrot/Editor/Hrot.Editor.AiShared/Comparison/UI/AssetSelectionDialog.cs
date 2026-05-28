using System.Numerics;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Comparison.UI;

/// <summary>
/// The result produced when the user confirms the asset selection dialog.
/// </summary>
public sealed record AssetSelectionResult(
    AssetExportRequest VersionA,
    AssetExportRequest VersionB,
    bool Reversed);

// ---------------------------------------------------------------------------

/// <summary>
/// Testable state model for <see cref="AssetSelectionDialog"/>.
/// Holds path inputs, validation results, and reversal state.
/// All business logic is here; no ImGui calls.
/// </summary>
public sealed class AssetSelectionDialogState
{
    /// <summary>Path for Version A (older).</summary>
    public string PathA { get; set; } = "";

    /// <summary>Path for Version B (newer).</summary>
    public string PathB { get; set; } = "";

    /// <summary>True when the user has pressed "Reverse A<->B" an odd number of times.</summary>
    public bool Reversed { get; private set; }

    /// <summary>Last validation error, or null when none.</summary>
    public string? ValidationError { get; private set; }

    /// <summary>Last validation warning, or null when none.</summary>
    public string? ValidationWarning { get; private set; }

    /// <summary>
    /// Swaps PathA and PathB and toggles the <see cref="Reversed"/> flag.
    /// </summary>
    public void Reverse()
    {
        (PathA, PathB) = (PathB, PathA);
        Reversed = !Reversed;
    }

    /// <summary>
    /// Validates the current PathA and PathB using <see cref="AssetSelectionValidator"/>.
    /// Sets <see cref="ValidationError"/> and <see cref="ValidationWarning"/> as side-effects.
    /// </summary>
    /// <returns>Null on success; error message string on failure.</returns>
    public string? Validate(AssetKind expectedKind)
    {
        ValidationError = null;
        ValidationWarning = null;

        var discoveredA = CompanionFileDiscovery.DiscoverFromMainFile(PathA, expectedKind);
        var discoveredB = CompanionFileDiscovery.DiscoverFromMainFile(PathB, expectedKind);

        var result = AssetSelectionValidator.Validate(discoveredA, discoveredB, expectedKind);

        foreach (var issue in result.Issues)
        {
            if (issue.Severity == ValidationSeverity.Error)
            {
                ValidationError = issue.Message;
                return issue.Message;
            }
        }

        foreach (var issue in result.Issues)
        {
            if (issue.Severity == ValidationSeverity.Warning)
            {
                ValidationWarning = issue.Message;
                break;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the result records using the current validated paths.
    /// Must only be called after <see cref="Validate"/> returned null.
    /// </summary>
    public AssetSelectionResult BuildResult(AssetKind expectedKind)
    {
        var versionA = new AssetExportRequest(
            PathA,
            Path.GetDirectoryName(PathA),
            expectedKind);

        var versionB = new AssetExportRequest(
            PathB,
            Path.GetDirectoryName(PathB),
            expectedKind);

        return new AssetSelectionResult(versionA, versionB, Reversed);
    }
}

// ---------------------------------------------------------------------------

/// <summary>
/// ImGui modal dialog that allows the user to select two asset versions for comparison.
/// The state model <see cref="AssetSelectionDialogState"/> is tested separately.
/// See design §7.1 and §7.2.
/// </summary>
public sealed class AssetSelectionDialog
{
    private readonly AssetSelectionDialogState _state = new();
    private bool _openPending;
    private bool _active;
    private bool _validated;

    /// <summary>Called once to request the dialog open on the next frame.</summary>
    public void RequestOpen(string prefillPathB = "")
    {
        _openPending = true;
        _state.PathA = "";
        _state.PathB = prefillPathB;
        _validated = false;
    }

    /// <summary>
    /// Renders the modal. Must be called every ImGui frame.
    /// Returns an <see cref="AssetSelectionResult"/> when the user confirms, null otherwise.
    /// </summary>
    public AssetSelectionResult? Render(AssetKind expectedKind)
    {
        if (_openPending)
        {
            ImGui.OpenPopup("Compare With...##assetsel");
            _openPending = false;
            _active = true;
        }

        if (!_active)
            return null;

        AssetSelectionResult? result = null;
        var modalOpen = true;

        if (ImGui.BeginPopupModal("Compare With...##assetsel", ref modalOpen,
            ImGuiWindowFlags.AlwaysAutoResize))
        {
            var pathA = _state.PathA;
            var pathB = _state.PathB;

            ImGui.TextDisabled("Version A (older):");
            if (ImGui.InputText("##pathA", ref pathA, 2048))
            {
                _state.PathA = pathA;
                _validated = false;
            }

            ImGui.TextDisabled("Version B (newer):");
            if (ImGui.InputText("##pathB", ref pathB, 2048))
            {
                _state.PathB = pathB;
                _validated = false;
            }

            if (ImGui.Button("Reverse A<->B"))
            {
                _state.Reverse();
                _validated = false;
            }

            ImGui.Separator();

            if (_state.ValidationError != null)
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), _state.ValidationError);
            else if (_state.ValidationWarning != null)
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.3f, 1f), _state.ValidationWarning);

            if (ImGui.Button("Validate"))
            {
                var err = _state.Validate(expectedKind);
                _validated = err == null;
            }

            ImGui.SameLine();

            if (!_validated) ImGui.BeginDisabled();
            if (ImGui.Button("Build Comparison Export"))
            {
                result = _state.BuildResult(expectedKind);
                ImGui.CloseCurrentPopup();
            }
            if (!_validated) ImGui.EndDisabled();

            ImGui.SameLine();

            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        if (!modalOpen)
            _active = false;

        return result;
    }
}
