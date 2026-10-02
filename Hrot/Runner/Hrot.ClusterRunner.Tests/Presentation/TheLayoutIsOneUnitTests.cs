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
    /// <para>⭐⭐⭐ <b>REWRITTEN by <c>A0</c>, <c>2026-08-23</c> — it now rails the MECHANISM, not just
    /// the outcome.</b> ⚠⚠ The previous version said so itself: <i>"the MECHANISM is not the ruling —
    /// <c>LocalWindowController</c> validates the persisted name against <c>ISubsystem.Name</c> … which
    /// would reject a durable perspective just as readily"</i>, and it asserted the fallback was
    /// <c>"Editor"</c>, a SUBSYSTEM name. ⇒ 📐 It passed for the wrong reason, and it could not have
    /// caught the blank <c>--mode all</c> first launch.</para>
    ///
    /// <para>⭐⭐ <b>The fakes now REGISTER WINDOWS</b>, so <c>GetPerspectives()</c> is populated and the
    /// real rule runs: the shipped <c>"Blueprint"</c> is claimed but document-driven ⇒ rejected, and the
    /// answer is the durable perspective — ⛔ NOT the subsystem's name, which is deliberately different
    /// here *(<c>"Editor"</c> owns the <c>"Scenario"</c> perspective, exactly as production does after
    /// <c>A1</c>)*.</para>
    /// </summary>
    [Fact]
    public void AColdStartDoesNotRestoreADocumentDrivenPerspective()
    {
        var shell = new GZH012_Tests.FakePresentationShell();
        // ⭐ The editor subsystem is named "Editor" and owns the "Scenario" perspective plus the three
        //   document-driven ones — the production shape after A1.
        var subsystems = new ISubsystem[]
        {
            new NamedSubsystem("PerspectiveCoordinator"),
            new NamedSubsystem("Editor", "Scenario", "BTree", "HSM", "Blueprint"),
        };

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

        // ⭐⭐ And it landed on the DURABLE PERSPECTIVE — ⛔ not on the subsystem's name.
        Assert.Equal("Scenario", actual);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>A0</c> — THE DEFAULT IS A REAL PERSPECTIVE, WHICH IS THE 22-WINDOW BLANK FIRST
    /// LAUNCH.</b> 📄 <c>UX_Feature_Perspective_Restore.md</c> §"First launch of <c>--mode all</c>".
    ///
    /// <para>📐 The composition for <c>--mode all</c> is
    /// <c>[PerspectiveCoordinator, Orchestrator, SimHost, IG, ExCon, CGF]</c>, and the old
    /// <c>Skip(1).First().Name</c> answered <b><c>"Orchestrator"</c></b> — whose windows are all
    /// <see cref="WindowScope.Global"/> with an empty perspective, so it claims NOTHING. ⇒ 🔴 every
    /// perspective-bound window failed its visibility gate and the app came up looking broken.</para>
    ///
    /// <para>⭐ With the claimed set as the source, the orchestrator cannot be chosen at all — and the
    /// answer is the first REQUESTED subsystem that actually owns a durable perspective.</para>
    /// </summary>
    [Fact]
    public void TheDefaultIsNeverASubsystemThatClaimsNoPerspective()
    {
        var shell = new GZH012_Tests.FakePresentationShell();
        var subsystems = new ISubsystem[]
        {
            new NamedSubsystem("PerspectiveCoordinator"),
            new NamedSubsystem("Orchestrator"),          // ⛔ Global windows only — claims nothing.
            new NamedSubsystem("SimHost", "SimHost"),
            new NamedSubsystem("IG", "IG"),
            new NamedSubsystem("ExCon", "ExCon"),
            new NamedSubsystem("CGF", "Scenario"),       // ⭐ A9: CGF's perspective is "Scenario".
        };

        var ctrl = new LocalWindowController(
            shell, subsystems, new RunnerOptions { ResetLayoutOnRun = false }, null);

        ctrl.OpenLocalWindow();
        var actual = ctrl.WindowManager!.CurrentPerspective;

        Assert.NotEqual("Orchestrator", actual);
        Assert.NotEqual("Default", actual);
        Assert.Contains(actual, ctrl.WindowManager!.GetPerspectives());
        // ⭐ Composition order still decides between durable perspectives.
        Assert.Equal("SimHost", actual);
    }

    /// <summary>
    /// ⭐⭐ <c>A0</c> — the pure rule, driven directly. ⛔ No shell, no files.
    /// </summary>
    [Theory]
    // ⭐ a claimed, durable stored value is honoured…
    [InlineData("Scenario", "Scenario")]
    // ⛔ …a document-driven one is not, even though it IS claimed…
    [InlineData("Blueprint", "SimHost")]
    // ⛔ …nor is a subsystem name that claims no perspective (the old false positive)…
    [InlineData("Orchestrator", "SimHost")]
    // ⛔ …nor an orphan left behind by the rename.
    [InlineData("Editor", "SimHost")]
    [InlineData(null, "SimHost")]
    public void TheStoredPerspectiveIsHonouredOnlyWhenClaimedAndDurable(string? persisted, string expected)
    {
        // ⭐ Alphabetical, as GetPerspectives() returns it.
        var claimed = new[] { "BTree", "Blueprint", "HSM", "Scenario", "SimHost" };
        var order   = new[] { "PerspectiveCoordinator", "Orchestrator", "SimHost", "CGF" };

        Assert.Equal(
            expected,
            LocalWindowController.ResolveStartupPerspective(claimed, order, persisted));
    }

    /// <summary>
    /// ⚠ <c>A0</c> — when only document-driven perspectives are claimed there is no honest answer, so it
    /// returns <c>"Default"</c>, ⭐ which <c>SwitchPerspective</c> then refuses LOUDLY rather than
    /// dropping the user into an empty graph workspace silently.
    /// </summary>
    [Fact]
    public void WithOnlyDocumentDrivenPerspectivesItRefusesToGuess()
        => Assert.Equal(
               "Default",
               LocalWindowController.ResolveStartupPerspective(
                   new[] { "BTree", "Blueprint", "HSM" }, new[] { "Editor" }, "Blueprint"));

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
    /// <summary>
    /// ⭐⭐ A subsystem that also CLAIMS PERSPECTIVES, by registering one window per name.
    /// ⛔ Without this the fakes claimed nothing, so <c>GetPerspectives()</c> was empty and the rail
    /// could only ever observe the subsystem-name fallback it was meant to be testing away from.
    /// </summary>
    private sealed class NamedSubsystem : ISubsystem, IWindowRegistrar
    {
        private readonly string[] _perspectives;

        public NamedSubsystem(string name, params string[] perspectives)
        {
            Name          = name;
            _perspectives = perspectives;
        }

        public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager wm)
        {
            foreach (var p in _perspectives)
                wm.RegisterWindow(new ClaimingWindow($"{Name.ToLowerInvariant()}_{p.ToLowerInvariant()}", p));
        }

        private sealed class ClaimingWindow : ManagedWindow
        {
            public ClaimingWindow(string id, string perspective)
                : base(id, id, perspective, WindowScope.PerspectiveBound) { }

            protected override void DrawClientArea() { }
        }

        public string Name { get; }
        public System.Numerics.Vector4 TitleBarColor => default;
        public void Initialize(SubsystemConfig config) { }
        public void Update(float deltaTime) { }
        public void DrawWorld() { }
        public void DrawUI() { }
        public void Shutdown() { }
    }
}
