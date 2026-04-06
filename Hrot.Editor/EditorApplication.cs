using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Toolkit.DER;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.Editor.Commands;
using Hrot.Editor.Events;
using Hrot.ScenarioEditor.Services;
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;

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

    public IDerRepo View => _view;
    public SimHostMode CurrentMode => _currentMode;

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

    /// <inheritdoc/>
    public void NewScenario() => _fileService.NewScenario(_world);

    /// <inheritdoc/>
    public void SaveScenario(string filePath) => _fileService.SaveScenario(_world, filePath);

    /// <inheritdoc/>
    public void LoadScenario(string filePath) => _fileService.LoadScenario(_world, filePath);

    /// <inheritdoc/>
    public void ActivateTool(EditorTool tool)
    {
        // Publish an FDP-managed event that the active tool controller listens for.
        // The actual tool switch logic lives in ScenarioEditorModule / IgApplication.
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
}
