using System;
using System.Linq;
using System.Numerics;
using Hrot.Editor.AiShared.Adapters;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Adapters;

/// <summary>
/// AIE-003 — ImGuiInputSource mapping tests.
/// Tests exercise the pure static helpers (MapMouseButton, MapEditorKey, MapModifiers)
/// which need no ImGui context.  Frame-snapshot members require a live context and
/// are verified structurally only (compile + non-throw headless guards).
/// </summary>
public sealed class AIE003_ImGuiInputSourceTests
{
    // ── AIE-003-01: MapMouseButton — all five buttons ─────────────────────────

    [Fact]
    public void ImGuiInputSource_Maps_AllMouseButtons()
    {
        Assert.Equal(ImGuiMouseButton.Left,   ImGuiInputSource.MapMouseButton(MouseButton.Left));
        Assert.Equal(ImGuiMouseButton.Right,  ImGuiInputSource.MapMouseButton(MouseButton.Right));
        Assert.Equal(ImGuiMouseButton.Middle, ImGuiInputSource.MapMouseButton(MouseButton.Middle));
        // X1/X2 are encoded as integer indices 3 and 4.
        Assert.Equal((ImGuiMouseButton)3,     ImGuiInputSource.MapMouseButton(MouseButton.X1));
        Assert.Equal((ImGuiMouseButton)4,     ImGuiInputSource.MapMouseButton(MouseButton.X2));
    }

    [Fact]
    public void ImGuiInputSource_Maps_MouseButton_Left_Distinct_From_Right()
    {
        // Sanity: different buttons map to different ImGui values.
        Assert.NotEqual(
            ImGuiInputSource.MapMouseButton(MouseButton.Left),
            ImGuiInputSource.MapMouseButton(MouseButton.Right));
    }

    // ── AIE-003-02: MapEditorKey — keys used by CanvasInput ───────────────────

    [Theory]
    [InlineData(EditorKey.Delete,    ImGuiKey.Delete)]
    [InlineData(EditorKey.Backspace, ImGuiKey.Backspace)]
    [InlineData(EditorKey.Escape,    ImGuiKey.Escape)]
    [InlineData(EditorKey.Tab,       ImGuiKey.Tab)]
    [InlineData(EditorKey.Space,     ImGuiKey.Space)]
    [InlineData(EditorKey.Left,      ImGuiKey.LeftArrow)]
    [InlineData(EditorKey.Right,     ImGuiKey.RightArrow)]
    [InlineData(EditorKey.Up,        ImGuiKey.UpArrow)]
    [InlineData(EditorKey.Down,      ImGuiKey.DownArrow)]
    [InlineData(EditorKey.Enter,     ImGuiKey.Enter)]
    [InlineData(EditorKey.Home,      ImGuiKey.Home)]
    [InlineData(EditorKey.End,       ImGuiKey.End)]
    [InlineData(EditorKey.PageUp,    ImGuiKey.PageUp)]
    [InlineData(EditorKey.PageDown,  ImGuiKey.PageDown)]
    public void ImGuiInputSource_Maps_CommonEditorKeys(EditorKey editorKey, ImGuiKey expected)
    {
        Assert.Equal(expected, ImGuiInputSource.MapEditorKey(editorKey));
    }

    [Fact]
    public void ImGuiInputSource_Maps_AllLetters_A_to_Z()
    {
        // Every letter key must map to a distinct, non-None ImGuiKey.
        var letters = new[]
        {
            EditorKey.A, EditorKey.B, EditorKey.C, EditorKey.D, EditorKey.E,
            EditorKey.F, EditorKey.G, EditorKey.H, EditorKey.I, EditorKey.J,
            EditorKey.K, EditorKey.L, EditorKey.M, EditorKey.N, EditorKey.O,
            EditorKey.P, EditorKey.Q, EditorKey.R, EditorKey.S, EditorKey.T,
            EditorKey.U, EditorKey.V, EditorKey.W, EditorKey.X, EditorKey.Y,
            EditorKey.Z,
        };

        var mapped = letters.Select(ImGuiInputSource.MapEditorKey).ToList();

        // None of them should be ImGuiKey.None.
        Assert.All(mapped, k => Assert.NotEqual(ImGuiKey.None, k));
        // All distinct.
        Assert.Equal(mapped.Count, mapped.Distinct().Count());
    }

    [Fact]
    public void ImGuiInputSource_Maps_Digits_D0_to_D9()
    {
        var digits = new[]
        {
            EditorKey.D0, EditorKey.D1, EditorKey.D2, EditorKey.D3, EditorKey.D4,
            EditorKey.D5, EditorKey.D6, EditorKey.D7, EditorKey.D8, EditorKey.D9,
        };

        var mapped = digits.Select(ImGuiInputSource.MapEditorKey).ToList();

        Assert.All(mapped, k => Assert.NotEqual(ImGuiKey.None, k));
        Assert.Equal(mapped.Count, mapped.Distinct().Count());
    }

    [Fact]
    public void ImGuiInputSource_Maps_FunctionKeys_F1_to_F12()
    {
        var fkeys = new[]
        {
            EditorKey.F1, EditorKey.F2, EditorKey.F3, EditorKey.F4,
            EditorKey.F5, EditorKey.F6, EditorKey.F7, EditorKey.F8,
            EditorKey.F9, EditorKey.F10, EditorKey.F11, EditorKey.F12,
        };

        var mapped = fkeys.Select(ImGuiInputSource.MapEditorKey).ToList();

        Assert.All(mapped, k => Assert.NotEqual(ImGuiKey.None, k));
        Assert.Equal(mapped.Count, mapped.Distinct().Count());
    }

    [Fact]
    public void ImGuiInputSource_Maps_Unknown_Key_To_None()
    {
        // EditorKey.Unknown → ImGuiKey.None (no crash).
        Assert.Equal(ImGuiKey.None, ImGuiInputSource.MapEditorKey(EditorKey.Unknown));
    }

    // ── AIE-003-03: MapModifiers ──────────────────────────────────────────────

    [Fact]
    public void ImGuiInputSource_Maps_Modifiers_None()
    {
        var mods = ImGuiInputSource.MapModifiers(false, false, false, false);
        Assert.Equal(KeyModifiers.None, mods);
    }

    [Fact]
    public void ImGuiInputSource_Maps_Modifiers_Ctrl()
    {
        var mods = ImGuiInputSource.MapModifiers(ctrl: true, shift: false, alt: false, super: false);
        Assert.Equal(KeyModifiers.Ctrl, mods);
    }

    [Fact]
    public void ImGuiInputSource_Maps_Modifiers_Shift()
    {
        var mods = ImGuiInputSource.MapModifiers(ctrl: false, shift: true, alt: false, super: false);
        Assert.Equal(KeyModifiers.Shift, mods);
    }

    [Fact]
    public void ImGuiInputSource_Maps_Modifiers_Alt()
    {
        var mods = ImGuiInputSource.MapModifiers(ctrl: false, shift: false, alt: true, super: false);
        Assert.Equal(KeyModifiers.Alt, mods);
    }

    [Fact]
    public void ImGuiInputSource_Maps_Modifiers_Super()
    {
        var mods = ImGuiInputSource.MapModifiers(ctrl: false, shift: false, alt: false, super: true);
        Assert.Equal(KeyModifiers.Super, mods);
    }

    [Fact]
    public void ImGuiInputSource_Maps_Modifiers_CtrlShift()
    {
        var mods = ImGuiInputSource.MapModifiers(ctrl: true, shift: true, alt: false, super: false);
        Assert.Equal(KeyModifiers.Ctrl | KeyModifiers.Shift, mods);
    }

    [Fact]
    public void ImGuiInputSource_Maps_Modifiers_CtrlAlt()
    {
        var mods = ImGuiInputSource.MapModifiers(ctrl: true, shift: false, alt: true, super: false);
        Assert.Equal(KeyModifiers.Ctrl | KeyModifiers.Alt, mods);
    }

    [Fact]
    public void ImGuiInputSource_Maps_Modifiers_AllDown()
    {
        var mods = ImGuiInputSource.MapModifiers(ctrl: true, shift: true, alt: true, super: true);
        Assert.Equal(KeyModifiers.Ctrl | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Super, mods);
    }

    // ── AIE-003-04: Interface implementation ─────────────────────────────────

    [Fact]
    public void ImGuiInputSource_Implements_IInputSource()
    {
        // Verifies the type is constructible and satisfies the interface.
        IInputSource source = new ImGuiInputSource();
        Assert.NotNull(source);
    }
}
