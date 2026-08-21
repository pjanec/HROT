using System.Collections.Generic;
using System.Linq;
using Fdp.Core.Logging;
using Fdp.Presentation.Windows;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Runner;
using Hrot.ClusterRunner.Systems;
using Hrot.Common;

namespace Hrot.ClusterRunner.Presentation;

internal sealed class LocalWindowController
{
    private readonly IPresentationShell _shell;
    private readonly IReadOnlyList<ISubsystem> _subsystems;
    private readonly RunnerOptions _options;
    private readonly PerspectiveCoordinatorSystem? _coordinator;

    private bool _isLocalWindowOpen;
    internal bool IsLocalWindowOpen => _isLocalWindowOpen;

    internal Fdp.Presentation.WindowManager.WindowManager? WindowManager { get; private set; }

    internal LocalWindowController(
        IPresentationShell shell,
        IReadOnlyList<ISubsystem> subsystems,
        RunnerOptions options,
        PerspectiveCoordinatorSystem? coordinator)
    {
        _shell       = shell;
        _subsystems  = subsystems;
        _options     = options;
        _coordinator = coordinator;
    }

    internal void OpenLocalWindow()
    {
        if (_isLocalWindowOpen) return;

        _shell.InitWindow(_options.WindowWidth, _options.WindowHeight, "HROT Cluster Runner", _options.TargetFps);

        // ⭐⭐⭐ Batch 103 (103a) — THE LAYOUT IS RESOLVED BEFORE ImGui IS SET UP, and in this order.
        //
        // ⛔⛔ ORDER IS LOAD-BEARING: SetupImGui installs the ini PATH and ImGui reads that file there
        //    and then. ⇒ a reset performed afterwards would land on disk and be ignored until the NEXT
        //    run — the classic "it works on the second launch" bug.
        //
        // ⭐ 1 — migrate first, so a user whose json still sits beside the exe keeps their arrangement
        //   even on a run where the reset is off. ⚠ Once: the new file's presence is the marker.
        if (LayoutPaths.TryMigrateLegacyWindowSettings(RaylibPresentationShell.AppFolderName))
            FdpLog<LocalWindowController>.Info(
                "[Layout] Migrated fdp_windows.json from beside the executable into the user layout folder.");

        // ⭐ 2 — then the reset, which force-overwrites what step 1 may have just placed. 🔒 The user's
        //   ruling: "copy the default to the user folder force-overwriting whatever was there, so ImGui
        //   loads the user copy as usual but reset to default."
        if (_options.ResetLayoutOnRun)
        {
            var copied = LayoutPaths.TryResetUserLayout(RaylibPresentationShell.AppFolderName);
            // ⭐⭐ It SAYS SO, every run. 📌 The design: "Reset ON is destructive by design — the user
            //    loses their arrangement every run, hence the discoverable indicator." ⛔ A silent
            //    destructive default is how a user spends an afternoon wondering why their layout keeps
            //    coming back.
            FdpLog<LocalWindowController>.Info(copied.Count > 0
                ? $"[Layout] Reset ON — restored the shipped default ({string.Join(", ", copied)}). "
                + "Use --reset-layout=false to keep your own arrangement."
                : "[Layout] Reset ON but no shipped default was found next to the executable — "
                + "keeping the existing user layout.");
        }

        _shell.SetupImGui();

        var atlas = _shell.LoadIconAtlas();
        _shell.LoadGizmoFont();
        var wm = new Fdp.Presentation.WindowManager.WindowManager(atlas);

        // Message log
        var messageLogRegistry = new MessageLogRegistry();
        messageLogRegistry.RegisterSource(NLogMessageLogTarget.SharedInstance);
        var msgLogWindow = new MessageLogWindow(messageLogRegistry);
        wm.RegisterWindow(msgLogWindow);
        wm.MessageLogRegistry = messageLogRegistry;

        // Register subsystem windows
        foreach (var sub in _subsystems)
            if (sub is IWindowRegistrar registrar)
                registrar.RegisterWindows(wm);

        if (_coordinator != null)
        {
            wm.OnPerspectiveChanged += (oldPersp, newPersp) =>
            {
                _coordinator.Enqueue(new TogglePerspectiveEvent(oldPersp, newPersp));
                Console.WriteLine($"[Runner] Perspective changed: {oldPersp} -> {newPersp}");
            };
        }

        RegisterLayoutMenu(wm);

        wm.StatusBar.RegisterSection("system_health", sortOrder: 0, () =>
        {
            ImGuiNET.ImGui.Text("System OK");
        });
        var msgLogSection = new MessageLogStatusBarSection(msgLogWindow, wm);
        wm.StatusBar.RegisterSection("msg_log_notify", sortOrder: 90, msgLogSection.Render);

        // ⭐⭐ Batch 103 (103a) — THE PATH IS PASSED. 📐 The seam already existed and was unused:
        //    LoadSettings/SaveSettings both take a path and NOBODY passed one, so the BaseDirectory
        //    fallback won and fdp_windows.json landed beside the exe — where a clean rebuild wipes it,
        //    and half a layout reset lives.
        // ⛔ No WindowManager change: this is the two call sites, as the design says.
        string settingsPath = LayoutPaths.UserWindowSettingsPath(RaylibPresentationShell.AppFolderName);
        string? persisted = wm.LoadSettings(settingsPath);

        // Wire the font pipeline: let the Settings UI drive live rescaling, and apply the
        // persisted UI-scale multiplier (queues a one-off rebake on the first frame if != 1).
        wm.FontService = _shell.FontService;
        _shell.FontService.SetUserScale(wm.UiScale);
        var first = _subsystems.Skip(1).FirstOrDefault();
        string defaultPersp = first?.Name ?? "Default";
        bool valid = !string.IsNullOrEmpty(persisted) && _subsystems.Any(s => s.Name == persisted);
        wm.SwitchPerspective(valid ? persisted! : defaultPersp);

        WindowManager = wm;
        _isLocalWindowOpen = true;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 103 (<c>103a</c>) — <c>File ▸ Layout</c>.</b>
    ///
    /// <para>⭐ Two entries, and the second exists <b>because the first is destructive</b>: 📌 the
    /// design's risk row — <i>"Reset ON is destructive by design — the user loses their arrangement
    /// every run, hence the discoverable indicator"</i>. ⇒ ⛔ a user who keeps losing their layout must
    /// be able to find out WHY without reading the source.</para>
    ///
    /// <para>⚠⚠ <b>"Disabled with a REASON, not hidden"</b> *(the design, verbatim)* — ⛔ and the menu
    /// renderer has <b>no tooltip support</b>: <c>RenderGlobalMenu</c> calls
    /// <c>Gui.MenuItem(label, shortcut, checked, enabled)</c> and nothing more. ⇒ ⭐ the reason travels
    /// in the LABEL, through <c>DynamicLabel</c>. ⛔ Teaching the shared renderer about tooltips is a
    /// change with every menu in the app downstream of it — not this batch's to make.</para>
    /// </summary>
    private void RegisterLayoutMenu(Fdp.Presentation.WindowManager.WindowManager wm)
    {
        // ⭐ The mode INDICATOR — checkable, so the current state is visible at a glance. ⚠ Toggling it
        //   changes the NEXT run: the reset already happened before ImGui was set up, and pretending
        //   otherwise would be a control that lies about when it acts.
        wm.GlobalMenu.RegisterCheckableItem(
            "File/Layout/Reset to default on start",
            () => _options.ResetLayoutOnRun,
            v =>
            {
                _options.ResetLayoutOnRun = v;
                FdpLog<LocalWindowController>.Info(
                    $"[Layout] Reset-on-start is now {(v ? "ON" : "OFF")} — it applies from the next run.");
            });

        wm.GlobalMenu.RegisterSeparator("File/Layout/sep_save_default");

        wm.GlobalMenu.RegisterItem("File/Layout/Save current as default", () =>
        {
            // ⭐⭐ Flush BOTH halves first — 📌 "the layout" is the pair, and saving one of them is the
            //    half-reset this batch exists to end.
            // ⚠ WindowManager's json is written on demand; ImGui only rewrites its ini at shutdown,
            //   so it is told to write NOW or the geometry copied would be the last run's.
            WindowManager?.SaveSettings(
                LayoutPaths.UserWindowSettingsPath(RaylibPresentationShell.AppFolderName));
            ImGuiNET.ImGui.SaveIniSettingsToDisk(
                LayoutPaths.UserIniPath(RaylibPresentationShell.AppFolderName));

            bool ok = LayoutPaths.TrySaveUserLayoutAsDefault(RaylibPresentationShell.AppFolderName);
            FdpLog<LocalWindowController>.Info(ok
                ? $"[Layout] Saved the current layout as the shipped default "
                + $"({LayoutPaths.TryFindSourceLayoutDirectory()}). Commit it to share it."
                : "[Layout] Could not save as default — no source tree was found above the executable.");
        });

        // ⛔ The command STAYS VISIBLE outside a checkout and says why it cannot run.
        var saveNode = wm.GlobalMenu.Root
            .Children["File"].Children["Layout"].Children["Save current as default"];
        saveNode.GetEnabled    = () => LayoutPaths.TryFindSourceLayoutDirectory() != null;
        saveNode.DynamicLabel  = () => LayoutPaths.TryFindSourceLayoutDirectory() != null
            ? "Save current as default"
            : "Save current as default (unavailable — not running from the source tree)";
    }

    internal void CloseLocalWindow()
    {
        if (!_isLocalWindowOpen) return;

        // ⭐ Exit saves to the USER location, as before — ⛔ only the location changed, not the moment.
        WindowManager?.SaveSettings(
            LayoutPaths.UserWindowSettingsPath(RaylibPresentationShell.AppFolderName));
        WindowManager = null;

        _shell.ShutdownImGui();
        _shell.CloseWindow();

        _isLocalWindowOpen = false;
    }
}
