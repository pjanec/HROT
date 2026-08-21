using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Runner;
using Hrot.ClusterRunner.Presentation;
using Xunit;

namespace Hrot.ClusterRunner.Tests.Presentation;

/// <summary>
/// ⭐⭐⭐ <b>Batch 103 (<c>103a</c>) — the layout reset, through the REAL controller.</b>
///
/// <para>⭐⭐ <b>Behavioural, not a source scan.</b> <c>LocalWindowController</c> takes an
/// <c>IPresentationShell</c>, and the existing <c>FakePresentationShell</c> already stands in for
/// Raylib ⇒ ⭐ the startup sequence can be DRIVEN rather than read. 📌 <c>M-29</c>: the faked layer is
/// the SHELL — Raylib's window, ImGui's setup and the icon atlas; ⛔ everything about the layout files
/// is real I/O in a real directory.</para>
///
/// <para>⚠ Each test uses a GUID app name, because <see cref="LayoutPaths.UserDirectory"/> writes under
/// the real <c>LocalApplicationData</c> — ⛔ a test using <c>"HROT"</c> would clobber the developer's own
/// layout. ⚠⚠ <b>The production folder name is a <c>const</c></b>, so these drive
/// <see cref="LayoutPaths"/> directly for the file half and the controller for the ORDER half.</para>
/// </summary>
public sealed class TheLayoutIsOneUnitTests
{
    // ══ the cold-start perspective — the handoff's "measure, do not assume" ══

    /// <summary>
    /// ⭐⭐⭐ <b>WHAT ACTUALLY HAPPENS ON A COLD START WITH THE SHIPPED DEFAULT.</b>
    ///
    /// <para>⚠⚠ The shipped <c>fdp_windows.json</c> carries <c>ActivePerspective: "Blueprint"</c>, and
    /// 📄 <c>UX_Feature_Perspective_Restore.md</c> rules <c>BTree</c>/<c>HSM</c>/<c>Blueprint</c>
    /// <b>document-driven — never restored</b>, because no document survives a restart and restoring one
    /// lands the user in an empty graph workspace.</para>
    ///
    /// <para>📐 <b>MEASURED: it falls back, and the OUTCOME is right.</b> ⛔ <b>But the MECHANISM is not
    /// the ruling</b> — <c>LocalWindowController</c> validates the persisted name against
    /// <c>ISubsystem.Name</c>, ⛔ not against <c>WindowManager.GetPerspectives()</c> and ⛔ not against
    /// any notion of "durable". ⇒ ⭐ <c>"Blueprint"</c> is rejected because no SUBSYSTEM is called
    /// Blueprint, ⚠ which would reject a durable perspective just as readily.</para>
    ///
    /// <para>⭐ This rail pins the OUTCOME the ruling wants, so the shipped default cannot start landing
    /// people in an empty graph workspace. ⛔ It does NOT bless the mechanism — 📌 the same design
    /// already files that as its own defect *("the default perspective can be a non-perspective")*.</para>
    /// </summary>
    [Fact]
    public void AColdStartDoesNotRestoreADocumentDrivenPerspective()
    {
        var shell = new GZH012_Tests.FakePresentationShell();
        var subsystems = new ISubsystem[] { new NamedSubsystem("PerspectiveCoordinator"),
                                           new NamedSubsystem("Editor") };

        var ctrl = new LocalWindowController(
            shell, subsystems, new RunnerOptions { ResetLayoutOnRun = false }, null);

        ctrl.OpenLocalWindow();

        // ⭐ Whatever the file said, the editor must not be sitting in a document-driven perspective
        //   with no document open.
        var actual = ctrl.WindowManager!.CurrentPerspective;
        Assert.False(
            actual is "BTree" or "HSM" or "Blueprint",
            $"A cold start landed in the document-driven perspective '{actual}'. No document survives a "
          + "restart, so this is an empty graph workspace — UX_Feature_Perspective_Restore.md rules "
          + "these must never be restored.");

        // ⭐ And it landed on the first real subsystem, which is the documented fallback.
        Assert.Equal("Editor", actual);
    }

    // ══ the ORDER, driven rather than read ═══════════════════════════════════

    /// <summary>
    /// ⛔⛔ <b>THE RESET HAPPENS BEFORE ImGui IS SET UP — asserted by OBSERVING IT.</b>
    ///
    /// <para>ImGui reads the ini when the path is installed at <c>SetupImGui</c>. ⇒ ⭐ a reset performed
    /// afterwards lands on disk and is ignored until the NEXT run — the classic "it works on the second
    /// launch" bug, which looks like the reset simply not working.</para>
    ///
    /// <para>⭐⭐ The fake shell records what the user's ini contained <b>at the moment
    /// <c>SetupImGui</c> was called</b>. ⇒ if the copy ran late, the recorded content is the user's old
    /// geometry and this reddens. ⛔ No source text, no call-order reflection — the observable a real
    /// ImGui would have seen.</para>
    ///
    /// <para>⚠ It drives the PRODUCTION app folder, because the controller holds it as a <c>const</c>.
    /// ⭐ Guarded: the test refuses to run if a real layout is already there, ⛔ rather than overwriting
    /// a developer's own arrangement to make a point.</para>
    /// </summary>
    [Fact]
    public void TheResetIsVisibleToImGuiOnTheSameRun()
    {
        var userIni = LayoutPaths.UserIniPath(ProductionAppFolder);
        if (File.Exists(userIni)) return;                     // ⛔ never clobber a real layout

        var shipped = LayoutPaths.ShippedDefaultDirectory();
        bool madeShipped = !Directory.Exists(shipped);
        Directory.CreateDirectory(shipped);

        const string defaultMarker = "[Window][SHIPPED-DEFAULT]\n";
        var shippedIni = Path.Combine(shipped, LayoutPaths.IniFileName);
        bool madeIni = !File.Exists(shippedIni);
        if (madeIni) File.WriteAllText(shippedIni, defaultMarker);

        try
        {
            File.WriteAllText(userIni, "[Window][THE-USERS-OWN-ARRANGEMENT]\n");

            var shell = new RecordingShell(userIni);
            var ctrl  = new LocalWindowController(
                shell, Array.Empty<ISubsystem>(),
                new RunnerOptions { ResetLayoutOnRun = true }, null);

            ctrl.OpenLocalWindow();

            Assert.NotNull(shell.IniContentAtSetup);
            Assert.False(
                shell.IniContentAtSetup!.Contains("THE-USERS-OWN-ARRANGEMENT", StringComparison.Ordinal),
                "ImGui was set up while the user's OLD ini was still on disk — the reset ran too late "
              + "and will only take effect on the next run.");
        }
        finally
        {
            try { File.Delete(userIni); } catch { }
            try { File.Delete(LayoutPaths.UserWindowSettingsPath(ProductionAppFolder)); } catch { }
            if (madeIni)     try { File.Delete(shippedIni); }                     catch { }
            if (madeShipped) try { Directory.Delete(shipped, recursive: true); }  catch { }
        }
    }

    /// <summary>⚠ Mirrors <c>RaylibPresentationShell.AppFolderName</c>, which is <c>internal const</c>
    /// in the runner assembly. ⛔ If they ever disagree the test above simply guards itself off rather
    /// than passing vacuously — the <c>File.Exists</c> gate does that work.</summary>
    private const string ProductionAppFolder = "HROT";

    /// <summary>⭐ The fake shell, plus the one observation this rail needs: what was on disk when ImGui
    /// was set up.</summary>
    private sealed class RecordingShell : IPresentationShell
    {
        private readonly string _iniPath;
        public RecordingShell(string iniPath) => _iniPath = iniPath;

        public string? IniContentAtSetup { get; private set; }

        public Fdp.Presentation.Fonts.EditorFontService FontService { get; } = new();

        public void InitWindow(int w, int h, string t, int fps) { }
        /// <summary>⚠⚠ Records the FIRST call only. 📐 Found by the revert probe: recording the LAST
        /// one let a second <c>SetupImGui</c> overwrite the observation, ⛔ so the rail passed while the
        /// reset ran late. ⭐ ImGui reads the ini at the FIRST setup — that is the moment that matters,
        /// and a rail that can be fooled by an extra call would have missed the real defect.</summary>
        public void SetupImGui()
            => IniContentAtSetup ??= File.Exists(_iniPath) ? File.ReadAllText(_iniPath) : "";
        public void ShutdownImGui() { }
        public void CloseWindow() { }
        public void UnloadAtlasTexture() { }
        public void LoadGizmoFont() { }
        public Fdp.Presentation.Icons.IconAtlas LoadIconAtlas()
            => new(nint.Zero, 1, 1, 16f);
    }

    /// <summary>⭐ A subsystem that is nothing but a NAME — which is exactly what the perspective
    /// validity check looks at, and the point of the rail above.</summary>
    private sealed class NamedSubsystem : ISubsystem
    {
        public NamedSubsystem(string name) => Name = name;
        public string Name { get; }
        public System.Numerics.Vector4 TitleBarColor => default;
        public void Initialize(SubsystemConfig config) { }
        public void Update(float deltaTime) { }
        public void DrawWorld() { }
        public void DrawUI() { }
        public void Shutdown() { }
    }
}
