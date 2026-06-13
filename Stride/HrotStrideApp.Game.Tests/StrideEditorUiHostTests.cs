#nullable enable
using System;
using HrotStrideApp;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// Stage-4.1 headless smoke tests for <see cref="StrideEditorUiHost"/>.
///
/// <para>
/// These tests run headlessly — no GPU, no Raylib, no rlImGui window.
/// They prove that the construction and registration path from
/// <see cref="EditorStrideSubsystem"/> (hosted mode) → <see cref="StrideEditorUiHost"/>
/// does not throw, and that <see cref="EditorStrideSubsystem.HostedEditorLogic"/>
/// is correctly exposed.
/// </para>
///
/// <para>
/// <b>What is NOT tested here:</b>
/// <see cref="StrideEditorUiHost.DrawEditorPanels"/> issues ImGui calls that require
/// an active ImGui context (created by <c>rlImGui.Setup</c> inside a live GLFW/OpenGL
/// window).  Those calls are GPU-deferred and verified by the user manually with both
/// flags set.
/// </para>
/// </summary>
public sealed class StrideEditorUiHostTests : IDisposable
{
    private readonly EditorStrideSubsystem _subsystem;

    public StrideEditorUiHostTests()
    {
        _subsystem = new EditorStrideSubsystem();
        // hostRealEditor=true boots the real EditorSubsystem headlessly (same as
        // EditorSubsystemHeadlessBootTests) and exposes HostedEditorLogic.
        _subsystem.Initialize(hostRealEditor: true);
    }

    public void Dispose() => _subsystem.Dispose();

    // ── HostedEditorLogic accessor ────────────────────────────────────────

    /// <summary>
    /// After Initialize(hostRealEditor=true), HostedEditorLogic is non-null.
    /// This is the precondition for StrideEditorUiHost construction.
    /// </summary>
    [Fact]
    public void HostedEditorLogic_AfterHostedInit_IsNonNull()
    {
        Assert.True(_subsystem.HostRealEditor,
            "HostRealEditor should be true when Initialize(hostRealEditor: true) was called.");
        Assert.NotNull(_subsystem.HostedEditorLogic);
    }

    // ── StrideEditorUiHost construction ───────────────────────────────────

    /// <summary>
    /// Constructing <see cref="StrideEditorUiHost"/> with a live
    /// <see cref="Hrot.Editor.IEditorLogic"/> does not throw.
    /// Panels are constructed but NOT drawn (no ImGui context needed for this test).
    /// </summary>
    [Fact]
    public void Constructor_WithLiveEditorLogic_DoesNotThrow()
    {
        var logic = _subsystem.HostedEditorLogic!;
        var host  = new StrideEditorUiHost(logic); // must not throw
        Assert.NotNull(host);
    }

    /// <summary>
    /// Constructing <see cref="StrideEditorUiHost"/> with a null logic throws
    /// <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void Constructor_NullEditorLogic_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new StrideEditorUiHost(null!));
    }

    // ── OFF path: HostedEditorLogic is null when not in hosted mode ────────

    /// <summary>
    /// When Initialize() is called without hostRealEditor=true,
    /// HostedEditorLogic returns null (OFF path is byte-identical).
    /// </summary>
    [Fact]
    public void HostedEditorLogic_OffPath_IsNull()
    {
        using var offSubsystem = new EditorStrideSubsystem();
        offSubsystem.Initialize(); // default: hostRealEditor=false
        Assert.Null(offSubsystem.HostedEditorLogic);
    }
}
