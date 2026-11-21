using System.Text.Json.Nodes;
using Fdp.Core.Logging;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario.Migrations.Helpers;

namespace Hrot.Common.Scenario.Migrations.Migrators.Scenario
{
    /// <summary>
    /// Migrates Hrot.Scenario from v2 to v3 by removing the retired BrainBlackboard component.
    /// </summary>
    /// <remarks>
    /// Schema change:
    /// - v2: an entity may carry BrainBlackboard { ...behaviour-specific seed values... }
    /// - v3: BrainBlackboard does not exist
    ///
    /// Why this migrator exists. P4 retired BrainBlackboard from the ComponentTypeRegistry and
    /// did not migrate the scenario corpus. ScenarioSerializer throws on a component name it
    /// cannot resolve — deliberately, to surface version skew and typos rather than silently
    /// dropping data — so every scenario still carrying the key became unloadable: the 2PC
    /// PrepareLive faulted and the commit was skipped. Measured 2026-09-28 on test-move and
    /// test-fire, neither of which had been loadable since P4 landed.
    ///
    /// The strictness is not the defect; the missing migrator is. Migration-system.md 2.1
    /// requires that a customer running engine v_n can load scenarios authored by any prior
    /// version, and 2.2 anticipates exactly this case — "component types are renamed, fields are
    /// added and removed ... with every engine release cycle". Deleting the key from the four
    /// scenarios in this repository fixes this repository; only a migrator fixes a customer's
    /// scenario library on a shared NAS.
    ///
    /// Up-migration: the key is dropped. This is lossy by construction — the component no longer
    /// exists, so there is nothing to carry the values into. Any behaviour that was seeded
    /// through the blackboard needs its parameters supplied another way; that is an authoring
    /// concern this migrator cannot solve, so it reports the loss per entity rather than
    /// discarding it silently.
    /// </remarks>
    internal sealed class V2ToV3_RemoveBrainBlackboard : IJsonDocumentMigrator
    {
        public string DocType => HrotDocumentTypes.Scenario;
        public int FromVersion => 2;
        public int ToVersion => 3;

        public void Apply(JsonObject root, MigrationContext ctx)
        {
            int removed = 0;
            int carriedData = 0;

            using (ctx.WithItem("entities"))
            {
                EntityPatch.OnEachEntity(root, (entityId, entity) =>
                {
                    using var __ = ctx.WithItem(entityId);

                    if (entity["BrainBlackboard"] is not JsonObject bb)
                        return;

                    // An empty block is pure dead weight; a populated one was seeding something,
                    // and saying so is the difference between a migration and a silent deletion.
                    if (bb.Count > 0)
                    {
                        carriedData++;
                        ctx.Report.AddNote(
                            $"Entity {entityId}: dropped BrainBlackboard carrying {bb.Count} " +
                            "seeded value(s); the component is retired and the values cannot be " +
                            "carried forward.");
                    }

                    entity.Remove("BrainBlackboard");
                    removed++;
                });
            }

            ctx.Report.AddNote(
                $"Removed the retired BrainBlackboard component from {removed} entities " +
                $"({carriedData} of them held seeded values).");
            FdpLog<V2ToV3_RemoveBrainBlackboard>.Info(
                "Scenario v2->v3: BrainBlackboard removed from {0} entities ({1} held data)",
                removed, carriedData);
        }
    }
}
