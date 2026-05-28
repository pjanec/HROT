using ImGuiNET;

namespace Hrot.Editor.AiShared.Comparison.UI;

/// <summary>
/// Testable state model for <see cref="PasteResponseModal"/>.
/// No ImGui calls; safe to use directly in unit tests.
/// See design §6.1.
/// </summary>
public sealed class PasteResponseModalState
{
    /// <summary>The raw text pasted by the user.</summary>
    public string PastedText { get; set; } = "";

    /// <summary>Parse error from the last <see cref="Apply"/> call, or null when none.</summary>
    public string? ParseError { get; private set; }

    /// <summary>True after a successful <see cref="Apply"/> call.</summary>
    public bool SessionWasApplied { get; private set; }

    /// <summary>
    /// Parses <see cref="PastedText"/> and, on success, stores the resulting
    /// <see cref="ComparisonSessionState"/> in <paramref name="registry"/> for
    /// <paramref name="assetId"/>.
    /// </summary>
    /// <returns>
    /// True when the response was successfully parsed and applied.
    /// False when parsing failed or the response contained only warnings with no changes.
    /// </returns>
    public bool Apply(Guid assetId, ComparisonSessionRegistry registry)
    {
        var response = LlmResponseParser.Parse(PastedText);

        // Only reject when the warning specifically indicates a structural parse failure
        // (truncated JSON). Informational notes do not invalidate a "nothing changed" result.
        string TruncationText = ComparisonErrorMessages.TruncatedResponse;
        var hasTruncationError = response.Warnings.Any(
            w => w.Contains(TruncationText, StringComparison.OrdinalIgnoreCase));

        if (hasTruncationError && response.Changes.Count == 0)
        {
            ParseError = response.Warnings.First(
                w => w.Contains(TruncationText, StringComparison.OrdinalIgnoreCase));
            return false;
        }

        ParseError = null;
        var state = new ComparisonSessionState(assetId, response);
        registry.SetSession(state);
        SessionWasApplied = true;
        return true;
    }

    /// <summary>Resets all state to initial values.</summary>
    public void Reset()
    {
        PastedText = "";
        ParseError = null;
        SessionWasApplied = false;
    }
}

// ---------------------------------------------------------------------------

/// <summary>
/// ImGui modal that lets the user paste the LLM's response text and apply it
/// as an active comparison session for the specified asset.
/// The state model <see cref="PasteResponseModalState"/> is tested separately.
/// See design §6.1.
/// </summary>
public sealed class PasteResponseModal
{
    private readonly PasteResponseModalState _state = new();
    private string _textBuf = "";
    private bool _openPending;
    private bool _active;

    /// <summary>Requests the modal to open on the next frame and resets state.</summary>
    public void RequestOpen()
    {
        _openPending = true;
        _state.Reset();
        _textBuf = "";
    }

    /// <summary>
    /// Renders the modal every frame.
    /// Returns true when the session was successfully applied (the caller should
    /// refresh canvas annotations or close dependent UI).
    /// </summary>
    public bool Render(Guid activeAssetId, ComparisonSessionRegistry registry)
    {
        if (_openPending)
        {
            ImGui.OpenPopup("Paste LLM Response##pastemod");
            _openPending = false;
            _active = true;
        }

        if (!_active)
            return false;

        var applied = false;
        var modalOpen = true;

        if (ImGui.BeginPopupModal("Paste LLM Response##pastemod", ref modalOpen,
            ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextDisabled("Paste the LLM response below:");

            ImGui.InputTextMultiline(
                "##lpastetext",
                ref _textBuf,
                256 * 1024,
                new System.Numerics.Vector2(600, 300));

            _state.PastedText = _textBuf;

            if (_state.ParseError != null)
                ImGui.TextColored(new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f), _state.ParseError);

            if (ImGui.Button("Apply"))
            {
                if (_state.Apply(activeAssetId, registry))
                {
                    applied = true;
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        if (!modalOpen)
            _active = false;

        return applied;
    }
}
