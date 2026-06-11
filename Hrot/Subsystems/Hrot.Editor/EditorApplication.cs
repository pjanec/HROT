using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.DER;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.Common.Events;
using Hrot.Editor.Commands;
using Hrot.Editor.Events;
using Hrot.Editor.Migration;
using Hrot.Editor.Modules;
using Hrot.ScenarioEditor.Services;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Editor;

/// <summary>
/// Implements <see cref="IEditorLogic"/> by delegating file operations to
/// <see cref="ScenarioFileService"/> and tool/command operations to
/// <see cref="FdpEventBus"/> events.
///
/// <para>
/// Panels must bind exclusively to <see cref="IEditorLogic"/> — no direct
/// field references to this class or any ECS/DDS types.
/// </para>
/// </summary>
public sealed class EditorApplication : IEditorLogic
{
    private readonly ScenarioFileService _fileService;
    private readonly FdpEventBus         _simBus;
    private readonly FdpEventBus         _orchestrationBus;
    private readonly EntityRepository    _world;
    private readonly DerRepo             _view = new(localNodeId: 0);

    private readonly ModuleHostKernel?          _kernel;
    private readonly IReadOnlyList<IEcsModule>? _logicPacks;
    private readonly IReadOnlyList<IEcsModule>? _translatorPacks;
    private readonly HotReloadMessageLogSource? _hotReloadSource;
    private readonly string[]                   _aiProjectPathSegments;
    private SimHostMode _currentMode = SimHostMode.Internal;
    private Fdp.Toolkit.Orchestration.ClusterState _currentClusterState = Fdp.Toolkit.Orchestration.ClusterState.Idle;
    private string? _pendingScenarioLoad;
    private bool _waitingForIdle;

    // ── Scenario tracking ─────────────────────────────────────────────────────

    private string? _loadedScenarioName;    private readonly MigrationAlertManager _alertManager = new();
    /// <summary>
    /// Optional delegate that returns the available scenario names.
    /// Injected by <see cref="SetAvailableScenariosSource"/> after construction to
    /// avoid a circular reference with Hrot.ClusterRunner.
    /// </summary>
    private Func<IReadOnlyList<string>>? _availableScenariosSource;

    public IDerRepo View => _view;
    public SimHostMode CurrentMode => _currentMode;

    /// <inheritdoc/>
    public string? LoadedScenarioName => _loadedScenarioName;

    /// <inheritdoc/>
    public IReadOnlyList<string> AvailableScenarios =>
        _availableScenariosSource?.Invoke() ?? Array.Empty<string>();

    /// <summary>
    /// Alert manager for migration events (degraded-mode banner, alert modal).
    /// </summary>
    internal MigrationAlertManager AlertManager => _alertManager;

    /// <inheritdoc/>
    public void Update()
    {
        foreach (var ev in _orchestrationBus.ReadManaged<Fdp.Toolkit.Orchestration.ClusterStateUpdateEvent>())
            _currentClusterState = ev.CurrentState;

        if (!_waitingForIdle || string.IsNullOrEmpty(_pendingScenarioLoad)) return;
        if (_currentClusterState != Fdp.Toolkit.Orchestration.ClusterState.Idle) return;

        _waitingForIdle = false;
        var scenarioName = _pendingScenarioLoad;
        _pendingScenarioLoad = null;

        // 1. Safely wipe the existing state (fires WorldResetEvent and SoftClears the repo)
        //    so the new scenario starts on a blank slate.
        NewScenario();

        // 2. Dispatch a cluster transition intent to route the load through the orchestrator.
        //    This triggers HrotEditLoadHandler -> StagingEntityExtractor -> NetworkSpawningSystem
        _orchestrationBus.PublishManaged(new Fdp.Toolkit.Orchestration.TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = Fdp.Toolkit.Orchestration.ClusterState.OperatingEdit,
            ScenarioId    = scenarioName,
            ExerciseId    = Guid.NewGuid()
        });

        _loadedScenarioName = scenarioName;
    }

    public EditorApplication(
        ScenarioFileService fileService,
        FdpEventBus simBus,
        FdpEventBus orchestrationBus,
        EntityRepository world,
        ModuleHostKernel?          kernel          = null,
        IReadOnlyList<IEcsModule>? logicPacks      = null,
        IReadOnlyList<IEcsModule>? translatorPacks = null,
        HotReloadMessageLogSource? hotReloadSource = null,
        string[]? aiProjectPathSegments            = null)
    {
        _fileService          = fileService      ?? throw new ArgumentNullException(nameof(fileService));
        _simBus               = simBus           ?? throw new ArgumentNullException(nameof(simBus));
        _orchestrationBus     = orchestrationBus ?? throw new ArgumentNullException(nameof(orchestrationBus));
        _world                = world            ?? throw new ArgumentNullException(nameof(world));
        _kernel               = kernel;
        _logicPacks           = logicPacks;
        _translatorPacks      = translatorPacks;
        _hotReloadSource      = hotReloadSource;
        _aiProjectPathSegments = aiProjectPathSegments
            ?? new[] { "Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj" };
    }

    /// <summary>
    /// Provides a source for the available scenario list (e.g. from ClusterUiCache).
    /// Call this after construction to avoid circular assembly references.
    /// </summary>
    public void SetAvailableScenariosSource(Func<IReadOnlyList<string>> source)
    {
        _availableScenariosSource = source;
    }

    /// <inheritdoc/>
    public void NewScenario()
    {
        _fileService.NewScenario(_world);
        _loadedScenarioName = null;
        _alertManager.OnScenarioCleared();
    }

    /// <inheritdoc/>
    public void SaveScenario(string filePath) => _fileService.SaveScenario(_world, filePath);

    /// <inheritdoc/>
    public void LoadScenario(string filePath)
    {
        _fileService.LoadScenario(_world, filePath);
        _alertManager.OnScenarioLoaded(_fileService.LastLoadResult);
    }

    /// <inheritdoc/>
    public void LoadScenarioByName(string scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return;
        _pendingScenarioLoad = scenarioName;
        _waitingForIdle = true;

        _orchestrationBus.PublishManaged(new Fdp.Toolkit.Orchestration.TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = Fdp.Toolkit.Orchestration.ClusterState.Idle
        });
    }

    /// <inheritdoc/>
    public void SaveCurrentScenario()
    {
        if (string.IsNullOrEmpty(_loadedScenarioName)) return;
        var dir = Path.Combine(EditorBootstrap.ScenariosRoot, _loadedScenarioName);
        Directory.CreateDirectory(dir);
        _fileService.SaveScenario(_world, Path.Combine(dir, "scenario.json"));
    }

    /// <inheritdoc/>
    public void SaveScenarioAs(string scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return;
        var dir = Path.Combine(EditorBootstrap.ScenariosRoot, scenarioName);
        Directory.CreateDirectory(dir);
        _fileService.SaveScenario(_world, Path.Combine(dir, "scenario.json"));
        _loadedScenarioName = scenarioName;
    }

    /// <inheritdoc/>
    public void ActivateTool(EditorTool tool)
    {
        // Publish an event that the active tool controller listens for.
        // The actual tool switch logic lives in EditorSubsystem.DrainToolActivationEvents().
        _simBus.Publish(new ActivateEditorToolEvent(tool));
    }

    /// <inheritdoc/>
    public void CommitPropertyEdit(long networkId, IReadOnlyList<object> updatedComponents)
    {
        if (updatedComponents == null) throw new ArgumentNullException(nameof(updatedComponents));
        _simBus.PublishManaged(new UpdateEntityCommand
        {
            NetworkId          = networkId,
            ComponentsToUpdate = new List<object>(updatedComponents),
        });
    }

    /// <inheritdoc/>
    public async Task SwitchToExternalAsync()
    {
        if (_kernel == null || _logicPacks == null) return;
        if (_currentMode == SimHostMode.External) return;

        await _kernel.UninstallModulesAsync(_logicPacks);

        if (_translatorPacks != null)
            await _kernel.InstallModulesAsync(_translatorPacks);

        _currentMode = SimHostMode.External;
    }

    /// <inheritdoc/>
    public async Task SwitchToInternalAsync()
    {
        if (_kernel == null || _logicPacks == null) return;
        if (_currentMode == SimHostMode.Internal) return;

        if (_translatorPacks != null)
            await _kernel.UninstallModulesAsync(_translatorPacks);

        await _kernel.InstallModulesAsync(_logicPacks);

        _currentMode = SimHostMode.Internal;
    }

    /// <inheritdoc/>
    public void CenterOnEntity(long entityId) =>
        _simBus.Publish(new CenterOnEntityCommand { NetworkId = entityId });

    /// <inheritdoc/>
    public void SelectEntity(long entityId) =>
        _simBus.Publish(new SelectEntityCommand { NetworkId = entityId });

    /// <inheritdoc/>
    public void OpenRenameDialog(long entityId) =>
        _simBus.Publish(new OpenRenameDialogCommand { NetworkId = entityId });

    /// <inheritdoc/>
    public void RebuildAndReloadAI()
    {
        // Fire-and-forget background compilation to keep the 60Hz UI responsive.
        // Once MSBuild overwrites the DLL, the FileSystemWatcher in FbtAssemblyHotReloader
        // will automatically detect it and swap the BTree interpreters via the ALC.
        Task.Run(() =>
        {
            string? projectPath = ResolveProjectFilePath(_aiProjectPathSegments);
            if (projectPath == null)
            {
                _hotReloadSource?.PushLine(
                    $"ERROR: AI Behaviors project file not found. Searched parent directories" +
                    $" from CWD for relative path: {Path.Combine(_aiProjectPathSegments)}");
                return;
            }

            _hotReloadSource?.PushLine($"Starting dotnet build: {projectPath}");

            var psi = new ProcessStartInfo
            {
                FileName               = "dotnet",
                Arguments              = $"build \"{projectPath}\"",
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = false };

            // Async line-by-line reading avoids the classic OS pipe-buffer deadlock
            // that occurs when both stdout and stderr fill up while the caller is
            // blocked on WaitForExit() with synchronous ReadToEnd().
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) _hotReloadSource?.PushLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) _hotReloadSource?.PushLine($"ERROR: {e.Data}");
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                _hotReloadSource?.PushLine($"Build exited with code {process.ExitCode}.");
            }
            catch (Exception ex)
            {
                _hotReloadSource?.PushLine($"ERROR: Failed to start dotnet process: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Traverses parent directories upward from <see cref="Environment.CurrentDirectory"/>
    /// until it finds a file at <paramref name="pathSegments"/> relative to that directory.
    /// Returns <c>null</c> if the file is not found in any ancestor.
    /// </summary>
    private static string? ResolveProjectFilePath(string[] pathSegments)
    {
        string relativePath = Path.Combine(pathSegments);
        string? dir = Environment.CurrentDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <inheritdoc/>
    public bool IsScenarioDegraded => _alertManager.IsDegradedMode;

    /// <inheritdoc/>
    public IReadOnlyList<SidecarFileInfo> GetMigrationSidecarsForCurrentScenario()
        => _fileService.GetSidecarsForLastLoadAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Returns an <see cref="EditorSystemsModule"/> initialised against this application's
    /// <see cref="EntityRepository"/>.  The caller must register the returned module with
    /// the kernel <em>before</em> calling <c>kernel.Initialize()</c>.
    /// </summary>
    public EditorSystemsModule CreateEditorSystemsModule()
        => new EditorSystemsModule();
}
