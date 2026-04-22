using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Scenario;
using Hrot.ScenarioEditor.Services;

namespace Hrot.Editor;

/// <summary>
/// Static factory for constructing the Editor's core services.
/// The full composition root (Raylib window, module kernel) lives in <c>Program.cs</c>
/// and is wired up in Phase 5 (PACK2-C001).
/// </summary>
public static class EditorBootstrap
{
    /// <summary>
    /// Root directory used for scenario files.
    /// Scenarios are stored as <c>{ScenariosRoot}\{scenarioName}\scenario.json</c>.
    /// </summary>
    public static string ScenariosRoot { get; } = OrchestrationConstants.DefaultStagingDirectory;

    /// <summary>
    /// Builds a <see cref="ScenarioFileService"/> with an auto-serializer
    /// configured for <c>"Hrot.Scenario"</c> subsystem type.
    /// </summary>
    public static ScenarioFileService CreateFileService()
    {
        // A minimal doctrine registry is created here so MissionPlanTranslator can
        // resolve BehaviorId -> DoctrineId on Inject. The editor uses a full registry
        // via EditorSubsystem; this factory creates an empty one consistent with the
        // SimHostApp Muscle-tier pattern (doctrines live on the Brain/CGF node).
        var doctrineRegistry = new DoctrineRegistry();

        var serializer = new ScenarioSerializerBuilder(HrotSubsystemTypes.Scenario)
            .RegisterTranslator(new Hrot.SimHost.Serializers.MissionPlanTranslator(doctrineRegistry))
            .RegisterTranslator(new Hrot.SimHost.Serializers.TargetMemoryTranslator())
            .RegisterTranslator(new Hrot.SimHost.Serializers.PassengerBufferTranslator())
            .RegisterTranslator(new Hrot.SimHost.Serializers.VisHierarchyNodeTranslator())
            .RegisterTranslator(new Hrot.SimHost.Serializers.IsEmbarkedTagTranslator())
            .RegisterTranslator(new Hrot.SimHost.Serializers.PersonalRouteRefTranslator())
            .Build();

        return new ScenarioFileService(serializer);
    }
}
