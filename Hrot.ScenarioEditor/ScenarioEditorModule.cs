using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

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
    public string Name => "ScenarioEditor";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    public void RegisterSystems(ISystemRegistry registry)
    {
        // Populated in PACK2-E002 (tool systems) and PACK2-E003 (render layer).
    }

    public void Tick(ISimulationView view, float deltaTime) { }
}
