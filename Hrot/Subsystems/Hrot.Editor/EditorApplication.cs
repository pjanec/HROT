using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Toolkit.DER;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.Editor.Commands;
using Hrot.Editor.Events;
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
    private readonly FdpEventBus         _bus;
    private readonly EntityRepository    _world;
    private readonly DerRepo             _view = new(localNodeId: 0);

    private readonly ModuleHostKernel?          _kernel;
    private readonly IReadOnlyList<IEcsModule>? _logicPacks;
    private readonly IReadOnlyList<IEcsModule>? _translatorPacks;
    private SimHostMode _currentMode = SimHostMode.Internal;

    // ── Scenario tracking ─────────────────────────────────────────────────────

    private string? _loadedScenarioName;

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

    public EditorApplication(
        ScenarioFileService fileService,
        FdpEventBus bus,
        EntityRepository world,
        ModuleHostKernel?          kernel          = null,
        IReadOnlyList<IEcsModule>? logicPacks      = null,
        IReadOnlyList<IEcsModule>? translatorPacks = null)
    {
        _fileService      = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _bus              = bus         ?? throw new ArgumentNullException(nameof(bus));
        _world            = world       ?? throw new ArgumentNullException(nameof(world));
        _kernel           = kernel;
        _logicPacks       = logicPacks;
        _translatorPacks  = translatorPacks;
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
    }

    /// <inheritdoc/>
    public void SaveScenario(string filePath) => _fileService.SaveScenario(_world, filePath);

    /// <inheritdoc/>
    public void LoadScenario(string filePath) => _fileService.LoadScenario(_world, filePath);

    /// <inheritdoc/>
    public void LoadScenarioByName(string scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return;
        var path = Path.Combine(EditorBootstrap.ScenariosRoot, scenarioName, "scenario.json");
        _fileService.LoadScenario(_world, path);
        _loadedScenarioName = scenarioName;
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
        // Publish an FDP-managed event that the active tool controller listens for.
        // The actual tool switch logic lives in EditorSubsystem.DrainToolActivationEvents().
        _bus.PublishManaged(new ActivateEditorToolEvent(tool));
    }

    /// <inheritdoc/>
    public void CommitPropertyEdit(long networkId, IReadOnlyList<object> updatedComponents)
    {
        if (updatedComponents == null) throw new ArgumentNullException(nameof(updatedComponents));
        _bus.PublishManaged(new UpdateEntityCommand
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
        _bus.PublishManaged(new CenterOnEntityCommand { NetworkId = entityId });

    /// <inheritdoc/>
    public void SelectEntity(long entityId) =>
        _bus.PublishManaged(new SelectEntityCommand { NetworkId = entityId });

    /// <inheritdoc/>
    public void OpenRenameDialog(long entityId) =>
        _bus.PublishManaged(new OpenRenameDialogCommand { NetworkId = entityId });

    /// <summary>
    /// Returns an <see cref="EditorSystemsModule"/> initialised against this application's
    /// <see cref="EntityRepository"/>.  The caller must register the returned module with
    /// the kernel <em>before</em> calling <c>kernel.Initialize()</c>.
    /// </summary>
    public EditorSystemsModule CreateEditorSystemsModule()
        => new EditorSystemsModule(_world);
}
