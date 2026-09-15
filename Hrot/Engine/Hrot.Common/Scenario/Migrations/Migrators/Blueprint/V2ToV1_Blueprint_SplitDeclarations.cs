using System.Text.Json.Nodes;
using Fdp.Core.Logging;
using Fdp.Core.Serialization.Migrations;
using Hrot.Blueprints.Core;

namespace Hrot.Common.Scenario.Migrations.Migrators.Blueprint
{
    /// <summary>
    /// Migrates <c>Hrot.Blueprint</c> from v2 back to v1 by splitting the tagged array into the three
    /// declaration lists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐⭐ <b>This is THE revert, and it is the reason the bump is safe to ship.</b> <c>git revert</c>
    /// puts the repo's 58 assets back, but it cannot reach a <c>.bp.json</c> outside the repo — a
    /// designer's working file, or a deployed asset, written as v2 by a newer editor and then opened by
    /// an older build. ⛔ Nothing else can undo that; this can.
    /// </para>
    /// <para>
    /// ⚠ <b>Lossless, unlike the Scenario pair it is modelled on.</b> <c>V2ToV1_EntityInfo_RemoveTags</c>
    /// drops a field and says so. Here every declaration survives with its kind, so
    /// <c>v2 → v1 → v2</c> and <c>v1 → v2 → v1</c> are both the identity.
    /// </para>
    /// <para>
    /// ⛔ <b>An untagged or unknown-tagged declaration THROWS rather than being dropped</b> — see
    /// <c>BlueprintSchemaV2.Down</c>. A dropped declaration is a field missing from the emitted
    /// struct, which changes the offsets of everything after it and re-initialises the blackboard of
    /// every entity running that blueprint.
    /// </para>
    /// </remarks>
    internal sealed class V2ToV1_Blueprint_SplitDeclarations : IJsonDocumentMigrator
    {
        public string DocType => HrotDocumentTypes.Blueprint;
        public int FromVersion => 2;
        public int ToVersion => 1;

        public void Apply(JsonObject root, MigrationContext ctx)
        {
            int count = BlueprintDeclarationRewrite.Apply(root, BlueprintSchemaV2.Down);

            ctx.Report.AddNote($"Split {count} declaration(s) back into the three v1 lists.");
            FdpLog<V2ToV1_Blueprint_SplitDeclarations>.Info(
                "Blueprint v2->v1: {0} declaration(s) split", count);
        }
    }
}
