#nullable enable
using System;
using HrotStrideApp;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// Headless smoke tests for the full-editor-UI wiring path
/// (<c>STRIDE_HOST_REAL_EDITOR=1</c> + <c>STRIDE_EDITOR_WINDOW=1</c>).
///
/// <para>
/// These tests run headlessly — no GPU, no Raylib, no rlImGui window.
/// They prove that:
/// <list type="bullet">
///   <item><c>EditorStrideSubsystem.HostedEditor</c> is non-null after hosted init.</item>
///   <item><c>buildEditorUi=true</c> sets the editor non-headless (enabling MapCanvas/adapters).</item>
///   <item><c>buildEditorUi=false</c> (default) keeps the editor headless — tests stay GL-free.</item>
///   <item>The OFF path (<c>hostRealEditor=false</c>) leaves <c>HostedEditor</c> null.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What is NOT tested here:</b>
/// <c>editor.RegisterWindows(wm)</c>, <c>editor.DrawWorld()</c>, and <c>editor.DrawUI()</c>
/// all require an active ImGui/GLFW context.  Those are GPU-deferred and verified by the
/// user manually with both flags on.
/// </para>
/// </summary>
public sealed class StrideEditorUiHostTests : IDisposable
{
    private readonly EditorStrideSubsystem _subsystem;

    public StrideEditorUiHostTests()
    {
        _subsystem = new EditorStrideSubsystem();
        // hostRealEditor=true, buildEditorUi=false (default):
        // Boots the real EditorSubsystem HEADLESSLY so no GL context is needed.
        // HostedEditor is non-null; HostedEditor is headless.
        _subsystem.Initialize(hostRealEditor: true);
    }

    public void Dispose() => _subsystem.Dispose();

    // ── HostedEditor accessor ─────────────────────────────────────────────────

    /// <summary>
    /// After Initialize(hostRealEditor=true), HostedEditor is non-null.
    /// This is the precondition for the window wiring (RegisterWindows/DrawWorld/DrawUI).
    /// </summary>
    [Fact]
    public void HostedEditor_AfterHostedInit_IsNonNull()
    {
        Assert.True(_subsystem.HostRealEditor,
            "HostRealEditor should be true when Initialize(hostRealEditor: true) was called.");
        Assert.NotNull(_subsystem.HostedEditor);
    }

    /// <summary>
    /// HostedEditorLogic is still exposed (backward compat) and non-null after hosted init.
    /// </summary>
    [Fact]
    public void HostedEditorLogic_AfterHostedInit_IsNonNull()
    {
        Assert.NotNull(_subsystem.HostedEditorLogic);
    }

    // ── buildEditorUi=false keeps the editor headless ─────────────────────────

    /// <summary>
    /// Default (buildEditorUi=false): the hosted EditorSubsystem is initialized with
    /// Headless=true so no GL context is required.  HostedEditor.IsHeadless returns true.
    /// </summary>
    [Fact]
    public void HostedEditor_DefaultBuildEditorUiFalse_IsHeadless()
    {
        // HostedEditor is non-null; verify it was booted headless
        // (IsHeadless is the public property on EditorSubsystem).
        var editor = _subsystem.HostedEditor;
        Assert.NotNull(editor);
        Assert.True(editor.IsHeadless,
            "When buildEditorUi=false (default), the hosted editor must be headless " +
            "so tests and CI never require a GL context.");
    }

    // ── OFF path: HostedEditor is null when not in hosted mode ────────────────

    /// <summary>
    /// When Initialize() is called without hostRealEditor=true,
    /// HostedEditor returns null (OFF path is byte-identical to pre-existing behaviour).
    /// </summary>
    [Fact]
    public void HostedEditor_OffPath_IsNull()
    {
        using var offSubsystem = new EditorStrideSubsystem();
        offSubsystem.Initialize(); // default: hostRealEditor=false
        Assert.Null(offSubsystem.HostedEditor);
        Assert.Null(offSubsystem.HostedEditorLogic);
    }

    // ── buildEditorUi=true (non-headless) wiring sanity ────────────────────────

    /// <summary>
    /// When Initialize(hostRealEditor=true, buildEditorUi=true) is called,
    /// the hosted EditorSubsystem is non-headless (IsHeadless=false).
    ///
    /// <para>
    /// NOTE: This test constructs and disposes a second subsystem that boots the editor
    /// non-headlessly.  Non-headless init constructs MapCanvas / adapters / layers but
    /// does NOT call any GL/Raylib functions (those are deferred to DrawWorld/DrawUI).
    /// So this remains safe for CI.
    /// </para>
    /// </summary>
    [Fact]
    public void HostedEditor_BuildEditorUiTrue_IsNonHeadless()
    {
        using var sub = new EditorStrideSubsystem();
        sub.Initialize(hostRealEditor: true, buildEditorUi: true);

        var editor = sub.HostedEditor;
        Assert.NotNull(editor);
        Assert.False(editor.IsHeadless,
            "When buildEditorUi=true, the hosted editor must be non-headless " +
            "so MapCanvas / adapters / layers are constructed and " +
            "RegisterWindows registers ALL editor panels.");
    }
}
