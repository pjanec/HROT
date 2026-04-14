using Fdp.Kernel;
using Hrot.ScenarioEditor.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.ScenarioEditor;

/// <summary>
/// Entry-point <see cref="IEcsModule"/> for the Scenario Editor shared interaction logic.
///
/// <para>
/// This stub will be populated in <c>PACK2-E002</c> (tool migration) and
/// <c>PACK2-E003</c> (render layer migration).
/// </para>
/// </summary>
public class ScenarioEditorModule : IEcsModule
{
    private readonly ScenarioFileService? _fileService;

    public ScenarioEditorModule(ScenarioFileService? fileService = null)
    {
        _fileService = fileService;
    }

    public string Name => "ScenarioEditor";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    /// <summary>
    /// Exposes the file service for use by panels that trigger New/Save/Load operations.
    /// <c>null</c> when no serializer was provided at construction time.
    /// </summary>
    public ScenarioFileService? FileService => _fileService;

    public void RegisterSystems(ISystemRegistry registry)
    {
        // Populated in PACK2-E002 (tool systems) and PACK2-E003 (render layer).
    }

    public void Tick(ISimulationView view, float deltaTime) { }
}
