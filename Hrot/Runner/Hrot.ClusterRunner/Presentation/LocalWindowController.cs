using System.Collections.Generic;
using System.Linq;
using Fdp.Core.Logging;
using Fdp.Presentation.Windows;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Runner;
using Hrot.ClusterRunner.Services;
using Hrot.ClusterRunner.Systems;
using Hrot.Common;
using Hrot.UI.Common.RunMode;

namespace Hrot.ClusterRunner.Presentation;

internal sealed class LocalWindowController
{
    private readonly IPresentationShell _shell;
    private readonly IReadOnlyList<ISubsystem> _subsystems;
    private readonly RunnerOptions _options;
    private readonly PerspectiveCoordinatorSystem? _coordinator;

    private bool _isLocalWindowOpen;
    internal bool IsLocalWindowOpen => _isLocalWindowOpen;

    /// <summary>⭐ CE-1290 — the window's title with nothing to name.</summary>
    internal const string BaseTitle = "HROT Cluster Runner";

    /// <summary>
    /// ⭐ CE-1290 — the title for a loaded scenario's name (pure): <c>HROT Cluster Runner — bt-doors</c>; with nothing to name, the base title.
    /// </summary>
    internal static string TitleFor(string? detail)
        => string.IsNullOrWhiteSpace(detail) ? BaseTitle : $"{BaseTitle} — {detail!.Trim()}";

    private readonly IWindowTitleSource[] _titleSources;
    private string _title = BaseTitle;

    /// <summary>The title the window carries now.</summary>
    internal string CurrentTitle => _title;

    internal Fdp.Presentation.WindowManager.WindowManager? WindowManager { get; private set; }

    /// <summary>⭐ <c>CE-4096</c> — the ONE start gate of this host (null while no window is open, and on an exempt host).</summary>
    internal StartGate? StartGate { get; private set; }

    /// <summary>⭐ <c>CE-4096</c> — the ONE start surface (the only window claiming <c>Start</c>).</summary>
    internal StartSurfaceWindow? StartSurface { get; private set; }

    /// <summary>⭐ <c>CE-4096</c> — the cluster-rest read the gate is fed each frame; a seam so a rail can drive the gate through a real
    /// controller. Default: <see cref="ClusterRestReader"/> over this host's subsystems.</summary>
    internal Func<ClusterRest>? ClusterRestOverride { get; set; }

    private bool _exitRequested;

    /// <summary>⭐ <c>CE-4096</c> — the Exit menu item / start-surface button asked to close: the frame loop treats it exactly like the
    /// window's [X] (every <c>IAppExitGuard</c> is asked first). Reads and clears the request.</summary>
    internal bool TakeExitRequest() { var r = _exitRequested; _exitRequested = false; return r; }

    internal void RequestExit() => _exitRequested = true;

    /// <summary>
    /// ⭐⭐ <c>CE-4101</c> follow-up — the frame loop's exit decision, ONCE at the top of a frame (so a rail drives the very policy the loop runs): <paramref name="requestedThisFrame"/> is the window's [X] or the Exit item
    /// (<see cref="TakeExitRequest"/>); the request asks <see cref="ExitGuard"/> (it may open a question and DEFER), and a deferred question that was answered <i>Discard / Save &amp; Exit</i> approves the exit on a later frame.
    /// <see langword="true"/> = leave the loop now; a Cancel anywhere keeps it <see langword="false"/>.
    /// </summary>
    internal bool ExitDue(bool requestedThisFrame)
    {
        if (requestedThisFrame && ExitGuard.OnExitRequested() != ExitDisposition.Deferred) return true;   // nothing to ask (or every question already answered in favour): exit now
        return ExitGuard.ExitApproved;
    }

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
        _titleSources = subsystems.OfType<IWindowTitleSource>().ToArray();
        ExitGuard = new ExitGuardChain(subsystems.OfType<IAppExitGuard>().ToArray());   // until the run controls are composed: the subsystems' own guards only
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4101</c> follow-up (AQ108 §2.11a ④) — <b>the process's ONE exit guard</b> (the frame loop asks it for the window's [X] and File › Exit): the subsystems' own guards (the editor's unsaved DOCUMENTS)
    /// first, then — when a run-controls owner with a scenario door was picked — the <see cref="ScenarioExit"/> guard over that owner's <see cref="ScenarioDoor"/>. Rebuilt by <see cref="ComposeRunControls"/>.
    /// </summary>
    internal IAppExitGuard ExitGuard { get; private set; }

    /// <summary>⭐ <c>CE-4101</c> follow-up — the ONE guard of the open scenario's exit (null before <see cref="ComposeRunControls"/>, or when no host offers a scenario door: SimHost alone has nothing of a scenario to lose).</summary>
    internal ScenarioExitGuard? ScenarioExit { get; private set; }

    /// <summary>
    /// The chain: documents first, the scenario door LAST — the door's approval is what stops the run / unloads the scenario, so that step runs only when nothing asked before it can still cancel the exit
    /// (a Cancel at the document question loses nothing; a Discard of documents followed by a Cancel at the scenario question would have lost them without an exit).
    /// </summary>
    private void ComposeExitGuard(IProvidesRunControls? owner)
    {
        var door = owner?.RunChoice?.Door;
        ScenarioExit = door is null ? null : new ScenarioExitGuard(door);
        var guards = _subsystems.OfType<IAppExitGuard>().ToList();
        if (ScenarioExit is not null) guards.Add(ScenarioExit);
        ExitGuard = new ExitGuardChain(guards);
    }

    /// <summary>⭐ <c>CE-1437</c> — the host whose run controls the process's ONE toolbar time control shows (null before <see cref="ComposeRunControls"/>, or when no host offers any).</summary>
    internal IProvidesRunControls? RunControlsOwner { get; private set; }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1437</c> (AQ108 §2.10 ①) — <b>the run controls, composed ONCE per process</b> beside <see cref="ComposeStartSurface"/>: registers the ONE toolbar time
    /// control over the offer <see cref="PickRunControls"/> picks. ⛔ The hosts no longer register it (last registration used to win — subsystem order). Called after every
    /// subsystem's <c>RegisterWindows</c>, when the hosts hold their transports.
    /// </summary>
    internal void ComposeRunControls(Fdp.Presentation.WindowManager.WindowManager wm)
    {
        var owner = PickRunControls(_subsystems.OfType<IProvidesRunControls>());
        RunControlsOwner = owner;
        if (owner?.Transport is { } transport)
            Hrot.UI.Common.Panels.ShellTimeControlToolbar.Register(wm.MainToolbar, transport, owner.RunChoice);
        ComposeExitGuard(owner);             // ⭐ CE-4101 follow-up — the ONE scenario exit guard, over the same owner's door
        owner?.ComposeScenarioActions(wm);   // ⭐ CE-1439 — the scenario menu (and so the Start surface's Open / New / Recording) of the host that owns ▶, by the same rule
    }

    /// <summary>
    /// ⭐ The pick RULE (AQ108 §2.10 ①, §2.12), never registration order: of the hosts that offer a transport, one whose choice HAS a scenario session (an operator: editor, CGF,
    /// Orchestrator) wins; among several operators the one that holds a WORLD of its own first — <see cref="OperatorRank"/> (editor, CGF, then the world-less Orchestrator);
    /// with no operator the first offered; none offered ⇒ <see langword="null"/> (no time control).
    /// </summary>
    internal static IProvidesRunControls? PickRunControls(IEnumerable<IProvidesRunControls> hosts)
    {
        var offered = hosts.Where(h => h.Transport is not null).ToList();
        var operators = offered.Where(h => h.RunChoice is { HasScenarioControl: true }).ToList();
        if (operators.Count > 0)
            return operators.OrderBy(h => OperatorRank(h.HostKind)).First();   // OrderBy is stable: equal ranks keep the subsystem order
        return offered.FirstOrDefault();
    }

    /// <summary>⭐ CE-1439 — among operator hosts the one with a local world to author in wins; the Orchestrator (the master's window, no world) is the fallback owner.</summary>
    private static int OperatorRank(RunHostKind kind) => kind switch
    {
        RunHostKind.Editor       => 0,
        RunHostKind.Cgf          => 1,
        RunHostKind.Orchestrator => 2,
        _                        => 3,
    };

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4096</c> (AQ108 §2.6) — <b>the start surface, composed ONCE for every cluster-runner host</b>: registers the one window that
    /// claims <c>Start</c>, builds the one gate and the Exit item. ⛔ Exempt: a process whose only subsystem is the Replay Browser
    /// (offline, no cluster) — it never rests in a cluster Idle. Extracted from <see cref="OpenLocalWindow"/> so a rail can compose against
    /// a bare <c>WindowManager</c>.
    /// </summary>
    internal void ComposeStartSurface(Fdp.Presentation.WindowManager.WindowManager wm)
    {
        var real = _subsystems.Where(s => s is not PerspectiveUpdateSubsystem).ToList();
        if (real.Count > 0 && real.All(s => s is Hrot.ReplayBrowser.ReplayBrowserSubsystem)) return;

        var gate = new StartGate(wm);
        var surface = new StartSurfaceWindow(wm.GlobalMenu, () => gate.Variant);
        wm.RegisterWindow(surface);
        // ⭐ Exit: the same path as the window's [X] (the frame loop asks every exit guard). Allowed in Idle — the Start surface offers it.
        wm.GlobalMenu.RegisterItem("File/Exit", RequestExit, allowedInIdle: true);
        StartGate = gate;
        StartSurface = surface;
        // ⭐ coordinator review of CE-4096 — the providers are built ONCE here and folded each frame (⛔ was: rebuilt every frame)
        _restProviders = ClusterRestReader.Providers(_subsystems);
        _restSources = _subsystems.OfType<IClusterRestSource>().ToArray();
    }

    private IReadOnlyList<Hrot.Presentation.DebugApi.ISubsystemDebugProvider> _restProviders = Array.Empty<Hrot.Presentation.DebugApi.ISubsystemDebugProvider>();
    private IClusterRestSource[] _restSources = Array.Empty<IClusterRestSource>();

    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>⭐ <c>CE-4096</c> — once a frame, BEFORE <c>WindowManager.Render()</c>: feeds the gate the cluster's rest. No-op with no gate.</summary>
    internal void TickStartGate() => TickStartGate(Clock.Elapsed.TotalSeconds);

    internal void TickStartGate(double nowSeconds)
        => StartGate?.Tick(nowSeconds, ClusterRestOverride?.Invoke() ?? ClusterRestReader.From(_restProviders, _restSources.Select(s => s.ReadClusterRest())));

    /// <summary>
    /// ⭐ CE-1290 — once a frame: composes the title from the first title source that has a detail (the editor's or CGF's loaded
    /// scenario; the runner never runs both) and sets the window's only when it CHANGED — a load names it, New / clear drops the name.
    /// A no-op while the window is closed (headless).
    /// </summary>
    internal void UpdateWindowTitle()
    {
        if (!_isLocalWindowOpen) return;
        string? detail = null;
        foreach (var source in _titleSources)
            if (!string.IsNullOrWhiteSpace(source.WindowTitleDetail)) { detail = source.WindowTitleDetail; break; }
        var title = TitleFor(detail);
        if (title == _title) return;
        _title = title;
        _shell.SetWindowTitle(title);
    }

    internal void OpenLocalWindow()
    {
        if (_isLocalWindowOpen) return;

        _title = BaseTitle;
        _shell.InitWindow(_options.WindowWidth, _options.WindowHeight, BaseTitle, _options.TargetFps);

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

        ComposeStartSurface(wm);   // ⭐ CE-4096
        ComposeRunControls(wm);    // ⭐ CE-1437 — the ONE toolbar time control, picked by rule

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
        wm.SwitchPerspective(ResolveStartupPerspective(wm, persisted));

        WindowManager = wm;
        _isLocalWindowOpen = true;
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>A0</c> — WHICH PERSPECTIVE THE APP OPENS ON, and it is a REAL one.</b>
    /// 📄 <c>docs/UX/UX_Feature_Perspective_Restore.md</c> §1–§2 · <c>DESIGN_Perspective_Unification.md</c>
    /// §3 <c>A0</c>.
    ///
    /// <para>🔴 <b>What was here before was measurably broken, and the rename would have made it
    /// fatal.</b> 📐 The old two lines picked <c>_subsystems.Skip(1).First().Name</c> — an
    /// <see cref="ISubsystem"/> NAME — and validated the persisted value against subsystem names too.
    /// ⛔⛔ <b>A subsystem name is not a perspective:</b> for <c>--mode all</c> it resolved to
    /// <c>"Orchestrator"</c>, whose three windows are all <see cref="WindowScope.Global"/> with an empty
    /// perspective ⇒ 🔴 <b>the documented 22-window blank first launch</b> *(§"First launch of
    /// <c>--mode all</c>")*. ⚠ <c>Skip(1)</c> also encoded <i>"skip the injected coordinator"</i>
    /// POSITIONALLY — reorder composition and the default silently moved.</para>
    ///
    /// <para>⭐⭐ <b>The claimed set is now the only source</b> *(<see cref="WindowManager.GetPerspectives"/>,
    /// its own doc: "the testable seam for perspective enumeration")*, and <c>RegisterWindows</c> has
    /// already run ~30 lines above, so it is populated. ⇒ ⛔ the coordinator cannot appear in it, which is
    /// what makes <c>Skip(1)</c> unnecessary rather than merely ugly.</para>
    ///
    /// <para>⛔⛔ <b>Document-driven perspectives are excluded from BOTH halves</b>
    /// *(<see cref="Hrot.Editor.AiShared.AssetKindExtensions.DocumentDrivenPerspectiveNames"/>)*.
    /// ⚠⚠ <b>The restore design only demanded it for RESTORE — measured `2026-08-23`, the DEFAULT needs it
    /// too, and only after <c>A1</c>:</b> 📐 <c>GetPerspectives()</c> is <c>OrderBy(p =&gt; p)</c>, so for
    /// <c>--mode editor</c> the claimed set sorts to <c>[Blueprint, BTree, HSM, Scenario]</c>
    /// *(culture comparison, so <c>Blueprint</c> precedes <c>BTree</c> — measured)* ⇒ a bare
    /// <c>known.First()</c> would open the editor in an <b>empty Blueprint graph</b>. 📌 That is a DEVIATION
    /// from §1 of the restore design, folded back into it.</para>
    ///
    /// <para>⭐ <b>Among the durable ones, composition order still wins</b> — it preserves today's
    /// <i>"first requested subsystem that owns one"</i> intent *(§1's recommendation)*. ⚠ It is now a
    /// PREFERENCE, not the rule: after <c>A1</c> the editor's subsystem is <c>"Editor"</c> while its
    /// perspective is <c>"Scenario"</c>, so the name match legitimately finds nothing and the first
    /// durable perspective is the answer.</para>
    /// </summary>
    private string ResolveStartupPerspective(
        Fdp.Presentation.WindowManager.WindowManager wm,
        string? persisted)
        => ResolveStartupPerspective(
               wm.GetPerspectives(),
               _subsystems.Select(s => s.Name).ToList(),
               persisted);

    /// <summary>
    /// ⭐⭐ <c>A0</c> — the pure decision, split out so a rail can drive it. ⛔ No ImGui, no shell.
    /// </summary>
    /// <param name="claimed">
    ///   <see cref="WindowManager.GetPerspectives"/> — the perspectives some registered window claims.
    /// </param>
    /// <param name="subsystemNamesInOrder">
    ///   Composition order, used only to PREFER one durable perspective over another. <c>null</c> at the
    ///   production call site is filled in from the live subsystem list.
    /// </param>
    /// <param name="persisted">The stored <c>ActivePerspective</c>, or <c>null</c>.</param>
    internal static string ResolveStartupPerspective(
        IReadOnlyList<string> claimed,
        IReadOnlyList<string>? subsystemNamesInOrder,
        string? persisted)
    {
        var documentDriven = Hrot.Editor.AiShared.AssetKindExtensions.DocumentDrivenPerspectiveNames;
        // ⭐ CE-4096 — Start is entered by the gate only, never a startup choice (it is in the claimed set so A0 allows the gate in).
        var durable = claimed.Where(p => !documentDriven.Contains(p) && p != Fdp.Presentation.WindowManager.WindowManager.StartPerspectiveName).ToList();

        // ⭐ 1 — honour the stored value, but only if it is CLAIMED and DURABLE.
        if (!string.IsNullOrEmpty(persisted)
            && claimed.Contains(persisted!)
            && !documentDriven.Contains(persisted!)
            && persisted != Fdp.Presentation.WindowManager.WindowManager.StartPerspectiveName)
            return persisted!;

        // ⭐ 2 — otherwise the first durable perspective a requested subsystem owns…
        if (subsystemNamesInOrder != null)
            foreach (var name in subsystemNamesInOrder)
                if (durable.Contains(name))
                    return name;

        // ⭐ 3 — …else simply the first durable one. ⚠ Falling through to a document-driven name is
        //   worse than "Default": at least "Default" is refused LOUDLY by A0.
        return durable.FirstOrDefault() ?? "Default";
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
    /// <summary>
    /// ⭐⭐ <b><c>VC-2</c> — the layout items live under <c>Settings</c>, not <c>File</c>.</b>
    /// 🔒 <b>User, visual check <c>2026-08-22</c>:</b> <i>"move <c>File ▸ Layout</c> to a
    /// <c>Settings</c> main menu."</i>
    ///
    /// <para>⭐⭐ <b>It joins the EXISTING <c>Settings</c> menu</b> — 📐 measured: the framework already
    /// had one *(<c>UI Scale &amp; Fonts…</c>)*, and <c>WindowManager</c> now registers it through
    /// <c>GlobalMenu</c> for exactly this reason. ⛔ Registering <c>"Settings/…"</c> while that item was
    /// still a DTO block would have drawn <b>two</b> top-level menus both called <c>Settings</c>, side
    /// by side — the two menu models render independently into one bar. 📌 <c>R-13</c>: route, do not
    /// duplicate.</para>
    ///
    /// <para>⚠ <b>No design record existed for this</b> — searched <c>docs/</c> and <c>.dev/</c> for a
    /// menu-placement design and found none; it is a user preference, recorded here and in the
    /// batch report rather than invented into a design doc.</para>
    /// </summary>
    private void RegisterLayoutMenu(Fdp.Presentation.WindowManager.WindowManager wm)
    {
        // ⭐ The mode INDICATOR — checkable, so the current state is visible at a glance. ⚠ Toggling it
        //   changes the NEXT run: the reset already happened before ImGui was set up, and pretending
        //   otherwise would be a control that lies about when it acts.
        wm.GlobalMenu.RegisterCheckableItem(
            "View/Settings/Layout/Reset to default on start",   // ⭐ CE-4085: Settings is a submenu of View
            () => _options.ResetLayoutOnRun,
            v =>
            {
                _options.ResetLayoutOnRun = v;
                FdpLog<LocalWindowController>.Info(
                    $"[Layout] Reset-on-start is now {(v ? "ON" : "OFF")} — it applies from the next run.");
            });

        wm.GlobalMenu.RegisterSeparator("View/Settings/Layout/sep_save_default");

        // ⭐ CE-1137 — the scene heard from the 3-D camera outside FPS (off by default, saved in the operator's audio.json). 🔒 User,
        //   2026-10-16: "a menu settings for enabling sounds in 3d map non-FPS mode heard from the camera position".
        wm.GlobalMenu.RegisterCheckableItem(
            "View/Settings/Sound in the 3-D view (from the camera)",
            () => Hrot.UI.Common.Audio.AudioPreferences.CameraSound,
            v => Hrot.UI.Common.Audio.AudioPreferences.CameraSound = v);

        wm.GlobalMenu.RegisterItem("View/Settings/Layout/Save current as default", () =>
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
            .Children["View"].Children["Settings"].Children["Layout"].Children["Save current as default"];
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
