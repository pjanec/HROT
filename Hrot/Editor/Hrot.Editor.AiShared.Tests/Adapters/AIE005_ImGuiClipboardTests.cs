using Hrot.Editor.AiShared.Adapters;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Adapters;

/// <summary>
/// AIE-005 — ImGuiClipboard tests.
/// Round-trip clipboard behavior requires a live ImGui context with a
/// registered backend and is deferred to manual/integration testing.
/// These tests verify compilation, interface conformance, and
/// headless-safe (non-throwing) behavior.
/// </summary>
public sealed class AIE005_ImGuiClipboardTests
{
    // ── AIE-005-01: Implements IClipboard ────────────────────────────────────

    [Fact]
    public void ImGuiClipboard_Implements_IClipboard()
    {
        IClipboard clipboard = new ImGuiClipboard();
        Assert.NotNull(clipboard);
    }

    // ── AIE-005-02: Headless non-throwing behavior ────────────────────────────

    [Fact]
    public void ImGuiClipboard_GetText_DoesNotThrow_WithoutImGuiContext()
    {
        var clipboard = new ImGuiClipboard();

        // Must not throw; returns null when there is no context.
        string? result = clipboard.GetText();

        // No assertion on value — result may be null (no context) or a string.
        // The important guarantee is: no exception.
    }

    [Fact]
    public void ImGuiClipboard_SetText_DoesNotThrow_WithoutImGuiContext()
    {
        var clipboard = new ImGuiClipboard();

        // Must not throw; silently discards when there is no context.
        clipboard.SetText("hello clipboard");
    }

    [Fact]
    public void ImGuiClipboard_SetText_Null_DoesNotThrow()
    {
        var clipboard = new ImGuiClipboard();

        // Null should be handled gracefully.
        clipboard.SetText(null!);
    }

    [Fact]
    public void ImGuiClipboard_SetText_Empty_DoesNotThrow()
    {
        var clipboard = new ImGuiClipboard();

        clipboard.SetText(string.Empty);
    }

    // NOTE: Round-trip verification (GetText returns what SetText wrote) requires
    // an active ImGui context with a platform clipboard backend registered.
    // That test is deferred to manual/integration testing in the editor shell.
}
