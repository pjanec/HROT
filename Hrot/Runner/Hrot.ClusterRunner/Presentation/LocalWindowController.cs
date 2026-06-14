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
        _shell.SetupImGui();

        var atlas = _shell.LoadIconAtlas();
        var wm = new Fdp.Presentation.WindowManager.WindowManager(atlas);

        // Message log
        var msgLogWindow = MessageLogHostWiring.CreateAndRegister(wm);

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

        wm.StatusBar.RegisterSection("system_health", sortOrder: 0, () =>
        {
            ImGuiNET.ImGui.Text("System OK");
        });
        MessageLogHostWiring.AddStatusBarNotifier(wm, msgLogWindow);

        string? persisted = wm.LoadSettings();
        var first = _subsystems.Skip(1).FirstOrDefault();
        string defaultPersp = first?.Name ?? "Default";
        bool valid = !string.IsNullOrEmpty(persisted) && _subsystems.Any(s => s.Name == persisted);
        wm.SwitchPerspective(valid ? persisted! : defaultPersp);

        WindowManager = wm;
        _isLocalWindowOpen = true;
    }

    internal void CloseLocalWindow()
    {
        if (!_isLocalWindowOpen) return;

        WindowManager?.SaveSettings();
        WindowManager = null;

        _shell.ShutdownImGui();
        _shell.CloseWindow();

        _isLocalWindowOpen = false;
    }
}
