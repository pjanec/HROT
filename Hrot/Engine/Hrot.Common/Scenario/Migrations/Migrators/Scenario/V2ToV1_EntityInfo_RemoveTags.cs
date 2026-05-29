using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario.Migrations.Helpers;

namespace Hrot.Common.Scenario.Migrations.Migrators.Scenario
{
    /// <summary>
    /// Migrates Hrot.Scenario from v2 to v1 by removing the Tags field from EntityInfo.
    /// </summary>
    /// <remarks>
    /// Schema change:
    /// - v2: EntityInfo { Name, ForceId, Tags: List&lt;string&gt; }
    /// - v1: EntityInfo { Name, ForceId }
    ///
    /// Down-migration removes Tags entirely. This is a lossy operation.
    /// Round-trip: Tags content cannot be recovered.
    /// </remarks>
    internal sealed class V2ToV1_EntityInfo_RemoveTags : IJsonDocumentMigrator
    {
        public string DocType => HrotDocumentTypes.Scenario;
        public int FromVersion => 2;
        public int ToVersion => 1;

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

                    if (info.Remove("Tags"))
                        count++;
                });
            }

            ctx.Report.AddNote($"Removed Tags from EntityInfo on {count} entities.");
        }
    }
}
