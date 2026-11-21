#nullable enable
using System;
using HrotStrideApp;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// Headless smoke tests for the full-editor-UI wiring path (<c>STRIDE_EDITOR_WINDOW=1</c>).
/// ⭐ CE-209: <c>STRIDE_HOST_REAL_EDITOR</c> is gone — hosting the real editor is no longer a mode
/// you select, it is the only composition, so only the window toggle remains.
///
/// <para>
/// These tests run headlessly — no GPU, no Raylib, no rlImGui window.
/// They prove that:
/// <list type="bullet">
///   <item><c>EditorStrideSubsystem.HostedEditor</c> is non-null after hosted init.</item>
///   <item><c>buildEditorUi=true</c> sets the editor non-headless (enabling MapCanvas/adapters).</item>
///   <item><c>buildEditorUi=false</c> (default) keeps the editor headless — tests stay GL-free.</item>
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
        // buildEditorUi=false (default): boots the real EditorSubsystem HEADLESSLY so no GL
        // context is needed. HostedEditor is non-null and headless.
        _subsystem.Initialize();
    }

    public void Dispose() => _subsystem.Dispose();

    // ── HostedEditor accessor ─────────────────────────────────────────────────

    /// <summary>
    /// After Initialize(), HostedEditor is non-null.
    /// This is the precondition for the window wiring (RegisterWindows/DrawWorld/DrawUI).
    /// </summary>
    [Fact]
    public void HostedEditor_AfterHostedInit_IsNonNull()
    {
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

    // ⛔ CE-209 — `HostedEditor_OffPath_IsNull` lived here and is DELETED, not re-homed. It
    //   asserted that Initialize() WITHOUT hostRealEditor leaves HostedEditor null — a test OF the
    //   self-contained arm. With that arm retired the claim is not merely unprovable, it is false by
    //   construction: every Initialize() hosts an editor. Deleting a test whose SUBJECT no longer
    //   exists is the right outcome; a weakened version would assert nothing.

    // ── buildEditorUi=true (non-headless) wiring sanity ────────────────────────

    /// <summary>
    /// When Initialize(buildEditorUi: true) is called,
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
        sub.Initialize(buildEditorUi: true);

        var editor = sub.HostedEditor;
        Assert.NotNull(editor);
        Assert.False(editor.IsHeadless,
            "When buildEditorUi=true, the hosted editor must be non-headless " +
            "so MapCanvas / adapters / layers are constructed and " +
            "RegisterWindows registers ALL editor panels.");
    }
}
