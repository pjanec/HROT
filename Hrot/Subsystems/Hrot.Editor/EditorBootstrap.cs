using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario;
using Hrot.Common.Scenario.Migrations;
using Hrot.Orchestrator;
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
    public static string ScenariosRoot => Path.Combine(ClusterConfiguration.Default.NasBasePath, OrchestrationConstants.ScenariosDirectoryName);

    /// <summary>
    /// Builds a <see cref="ScenarioFileService"/> with an auto-serializer
    /// configured for <c>"Hrot.Scenario"</c> subsystem type.
    /// </summary>
    public static ScenarioFileService CreateFileService()
    {
        // A minimal behavior registry is created here so MissionPlanTranslator can
        // resolve BehaviorId -> BehaviorId on Inject. The editor uses a full registry
        // via EditorSubsystem; this factory creates an empty one consistent with the
        // SimHostApp Muscle-tier pattern (behaviors live on the Brain/CGF node).
        var behaviorRegistry = new BehaviorRegistry();

        var serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(behaviorRegistry);

        var migrations = HrotMigrationBootstrap.BuildEditor();
        return new ScenarioFileService(serializer, migrationServices: migrations);
    }

    /// <summary>Creates the full Editor MigrationServices bundle.</summary>
    public static MigrationServices CreateMigrationServices() =>
        HrotMigrationBootstrap.BuildEditor();
}
