using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;

namespace Hrot.Common.Scenario.Migrations.Migrators.Scenario
{
    /// <summary>
    /// Migrates Hrot.Scenario from v3 back to v2. Deliberately a no-op.
    /// </summary>
    /// <remarks>
    /// The chain needs a v3-&gt;v2 step so a scenario authored under v3 can be opened by an engine
    /// still on v2 (Migration-system.md 2.2, "backward compatibility (revert)"). There is nothing
    /// for it to do, and that is the correct outcome rather than a gap:
    ///
    /// - BrainBlackboard cannot be reconstructed. Its values were dropped going up, by
    ///   construction — the component was retired, so nothing carried them.
    /// - Its ABSENCE is not an error on a v2 engine. ScenarioSerializer throws on a component key
    ///   it cannot resolve, never on one that is simply missing; an entity without the component
    ///   loads cleanly. So a v3 document is already valid input to v2.
    /// - Re-inserting an empty BrainBlackboard {} would therefore buy nothing and would push a
    ///   meaningless block back into a customer's file on every down-migration.
    ///
    /// Round-trip v2 -&gt; v3 -&gt; v2 is consequently lossy in exactly one direction, and the loss
    /// happened in V2ToV3_RemoveBrainBlackboard, which reports it per entity.
    /// </remarks>
    internal sealed class V3ToV2_RestoreBrainBlackboard : IJsonDocumentMigrator
    {
        public string DocType => HrotDocumentTypes.Scenario;
        public int FromVersion => 3;
        public int ToVersion => 2;

        public void Apply(JsonObject root, MigrationContext ctx)
        {
            ctx.Report.AddNote(
                "BrainBlackboard is retired and cannot be restored; a v2 engine loads entities " +
                "without it, so no change is needed.");
        }
    }
}
