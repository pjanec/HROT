using System;
using System.Collections.Generic;
using Fdp.Kernel;
using FDP.Toolkit.DER;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.Editor.Events;
using Hrot.ScenarioEditor.Services;

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

    public IDerRepo View => _view;

    public EditorApplication(
        ScenarioFileService fileService,
        FdpEventBus bus,
        EntityRepository world)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _bus         = bus         ?? throw new ArgumentNullException(nameof(bus));
        _world       = world       ?? throw new ArgumentNullException(nameof(world));
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
}
