using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Scenario;
using Hrot.Common.Scenario.Migrations;
using Hrot.Common.Scenario.Migrations.Migrators.Scenario;
using Xunit;

namespace Hrot.Common.Tests.Migrations
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-412</c> — the scenario schema version has TWO PRODUCERS, and this is the one
    /// place that can see both.</b>
    ///
    /// <para><c>ScenarioSerializer.CurrentSchemaVersion</c> (Fdp.Toolkits) is what a SAVE stamps
    /// into <c>$meta</c>. <c>ScenarioMigrationModule.CurrentVersion</c> (Hrot.Common) is the
    /// highest version the migration CHAIN understands. ⛔ Toolkits cannot reference Hrot.Common,
    /// so the duplication is structural and cannot be collapsed into one symbol — which is
    /// exactly the shape this repository keeps filing as a defect when nothing pins it.</para>
    ///
    /// <para>🔴 <b>What drift costs.</b> If the save stamp is LOWER than the chain's current
    /// version, every freshly written file is immediately "out of date" and gets migrated on the
    /// next load — work that should never happen, and a silent lie about the file's shape. If it
    /// is HIGHER, the chain has no migrator for the version being claimed. 📌 This nearly
    /// happened: bumping the chain to v3 for <see cref="V2ToV3_RemoveBrainBlackboard"/>
    /// left the save stamp at 2 until this rail was written.</para>
    /// </summary>
    public sealed class ScenarioSchemaVersionAgreementTests
    {
        [Fact]
        public void TheSaveStampAndTheMigrationChainAgreeOnTheCurrentVersion()
        {
            Assert.Equal(ScenarioMigrationModule.CurrentVersion, ScenarioSerializer.CurrentSchemaVersion);
        }

        /// <summary>
        /// ⭐ The v2 → v3 migrator drops the retired <c>BrainBlackboard</c> and leaves everything
        /// else on the entity untouched. This is what makes a customer's unmigrated scenario
        /// loadable again; deleting the key from the four scenarios in this repository only fixes
        /// this repository.
        /// </summary>
        [Fact]
        public void V2ToV3_DropsBrainBlackboard_AndTouchesNothingElse()
        {
            var root = new JsonObject
            {
                ["entities"] = new JsonObject
                {
                    ["e1"] = new JsonObject
                    {
                        ["BrainBlackboard"] = new JsonObject { ["X"] = 489, ["Speed"] = 5 },
                        ["SimTransform"]    = new JsonObject { ["Position"] = new JsonArray(1, 2, 3) },
                    },
                    ["e2"] = new JsonObject
                    {
                        ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(4, 5, 6) },
                    },
                },
            };

            new V2ToV3_RemoveBrainBlackboard()
                .Apply(root, NewContext());

            var e1 = (JsonObject)root["entities"]!["e1"]!;
            var e2 = (JsonObject)root["entities"]!["e2"]!;

            Assert.False(e1.ContainsKey("BrainBlackboard"));   // ⭐ dropped
            Assert.True(e1.ContainsKey("SimTransform"));       // ⛔ and nothing else touched
            Assert.True(e2.ContainsKey("SimTransform"));       // ⛔ an entity without it is untouched
        }

        /// <summary>
        /// ⭐ Idempotent: running the migrator over a document that has already been migrated is a
        /// no-op rather than an error. A scenario can reach v3 either by migration or by being
        /// saved fresh, and both must be valid input.
        /// </summary>
        [Fact]
        public void V2ToV3_IsIdempotent()
        {
            var root = new JsonObject
            {
                ["entities"] = new JsonObject
                {
                    ["e1"] = new JsonObject { ["SimTransform"] = new JsonObject() },
                },
            };

            var migrator = new V2ToV3_RemoveBrainBlackboard();
            migrator.Apply(root, NewContext());
            migrator.Apply(root, NewContext());

            Assert.True(((JsonObject)root["entities"]!["e1"]!).ContainsKey("SimTransform"));
        }

        private static MigrationContext NewContext()
            => new MigrationContext(HrotDocumentTypes.Scenario, null);
    }
}
