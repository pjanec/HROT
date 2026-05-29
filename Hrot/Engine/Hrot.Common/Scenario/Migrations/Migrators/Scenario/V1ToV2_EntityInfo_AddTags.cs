using System.Text.Json.Nodes;
using Fdp.Core.Logging;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario.Migrations.Helpers;

namespace Hrot.Common.Scenario.Migrations.Migrators.Scenario
{
    /// <summary>
    /// Migrates Hrot.Scenario from v1 to v2 by adding a Tags field to EntityInfo.
    /// </summary>
    /// <remarks>
    /// Schema change:
    /// - v1: EntityInfo { Name, ForceId }
    /// - v2: EntityInfo { Name, ForceId, Tags: List&lt;string&gt; }
    ///
    /// Up-migration default: Tags = [].
    /// Down-migration: Tags field removed (information loss).
    /// Round-trip: lossy (v_higher Tag content cannot be recovered from v_lower).
    /// </remarks>
    internal sealed class V1ToV2_EntityInfo_AddTags : IJsonDocumentMigrator
    {
        public string DocType => HrotDocumentTypes.Scenario;
        public int FromVersion => 1;
        public int ToVersion => 2;

        public void Apply(JsonObject root, MigrationContext ctx)
        {
            int count = 0;

            using (ctx.WithItem("entities"))
            {
                EntityPatch.OnEachEntity(root, (entityId, entity) =>
                {
                    using var __ = ctx.WithItem(entityId);

                    if (entity["EntityInfo"] is not JsonObject info)
                        return;

                    if (info.ContainsKey("Tags"))
                    {
                        // Idempotent: don't overwrite if already present
                        // (shouldn't happen in a clean v1 file but defends against
                        // pre-migrated content)
                        return;
                    }

                    info["Tags"] = new JsonArray();
                    count++;
                });
            }

            ctx.Report.AddNote($"Added empty Tags array to EntityInfo on {count} entities.");
            FdpLog<V1ToV2_EntityInfo_AddTags>.Info(
                "Scenario v1->v2: EntityInfo.Tags added on {0} entities", count);
        }
    }
}
