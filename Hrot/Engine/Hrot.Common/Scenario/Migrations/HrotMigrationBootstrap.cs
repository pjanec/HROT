using Fdp.Core;
using Fdp.Core.Serialization.Migrations;

namespace Hrot.Common.Scenario.Migrations
{
    /// <summary>
    /// Role-specific factory for <see cref="MigrationServices"/>.
    /// Each host process (SimHost, CGF, IG, Editor, ClusterRunner) calls the
    /// appropriate method once during startup.
    /// <para>Enforces M-2: each host registers only the formats it actually loads.</para>
    /// </summary>
    public static class HrotMigrationBootstrap
    {
        /// <summary>
        /// Creates <see cref="MigrationServices"/> for a SimHost or CGF node.
        /// Registers: Scenario, TKB, RoadNetwork (all read-only) + OrchestratorContext passthrough.
        /// </summary>
        public static MigrationServices BuildSimHostCgf(string writerIdentifier = "Hrot.SimHost")
        {
            return MigrationBootstrap.BuildForProduction(reg =>
            {
                ScenarioMigrationModule.RegisterAll(reg);
                TkbMigrationModule.RegisterAll(reg);
                RoadNetworkMigrationModule.RegisterAll(reg);
                reg.RegisterPassthroughDocType(HrotDocumentTypes.OrchestratorContext, 2);
            }, writerIdentifier);
        }

        /// <summary>
        /// Creates <see cref="MigrationServices"/> for an IG (Image Generator) node.
        /// Registers: Scenario, TKB (read-only) + OrchestratorContext + MapInteractionConfig passthroughs.
        /// Blueprint and BehaviorTree are intentionally NOT registered (M-2).
        /// </summary>
        public static MigrationServices BuildIg()
        {
            return MigrationBootstrap.BuildForProduction(reg =>
            {
                ScenarioMigrationModule.RegisterAll(reg);
                TkbMigrationModule.RegisterAll(reg);
                reg.RegisterPassthroughDocType(HrotDocumentTypes.OrchestratorContext, 2);
                reg.RegisterPassthroughDocType(HrotDocumentTypes.MapInteractionConfig, 1);
            }, "Hrot.IG");
        }

        /// <summary>
        /// Creates <see cref="MigrationServices"/> for the Editor host.
        /// Registers all customer-facing formats (both adapters) plus all HROT passthrough formats.
        /// </summary>
        public static MigrationServices BuildEditor()
        {
            return MigrationBootstrap.BuildForProduction(reg =>
            {
                ScenarioMigrationModule.RegisterAll(reg);
                BlueprintMigrationModule.RegisterAll(reg);
                BehaviorTreeMigrationModule.RegisterAll(reg);
                TkbMigrationModule.RegisterAll(reg);
                RoadNetworkMigrationModule.RegisterAll(reg);
                PassthroughFormatsModule.RegisterAll(reg);
            }, "Hrot.Editor");
        }

        /// <summary>
        /// Creates <see cref="MigrationServices"/> for <c>Hrot.ClusterRunner --mode migrate</c>.
        /// Same profile as Editor (persistent adapter), different writer identifier.
        /// </summary>
        public static MigrationServices BuildClusterRunnerMigrate()
        {
            return MigrationBootstrap.BuildForProduction(reg =>
            {
                ScenarioMigrationModule.RegisterAll(reg);
                BlueprintMigrationModule.RegisterAll(reg);
                BehaviorTreeMigrationModule.RegisterAll(reg);
                TkbMigrationModule.RegisterAll(reg);
                RoadNetworkMigrationModule.RegisterAll(reg);
                PassthroughFormatsModule.RegisterAll(reg);
            }, "Hrot.ClusterRunner --mode migrate");
        }

        /// <summary>
        /// Creates <see cref="MigrationServices"/> for <c>Hrot.ClusterRunner --mode ci</c>.
        /// Same as SimHost plus TestScript and NodeConfiguration passthroughs.
        /// </summary>
        public static MigrationServices BuildClusterRunnerCi()
        {
            return MigrationBootstrap.BuildForProduction(reg =>
            {
                ScenarioMigrationModule.RegisterAll(reg);
                TkbMigrationModule.RegisterAll(reg);
                RoadNetworkMigrationModule.RegisterAll(reg);
                reg.RegisterPassthroughDocType(HrotDocumentTypes.OrchestratorContext, 2);
                reg.RegisterPassthroughDocType(HrotDocumentTypes.TestScript, 1);
                reg.RegisterPassthroughDocType(HrotDocumentTypes.NodeConfiguration, 1);
            }, "Hrot.ClusterRunner --mode ci");
        }
    }
}
