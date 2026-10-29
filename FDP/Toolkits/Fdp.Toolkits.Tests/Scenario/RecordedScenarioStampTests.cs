using System;
using System.IO;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkits.Tests.Scenario
{
    /// <summary>
    /// ⭐⭐ CE-3340 ② (AQ108 D7) — <see cref="RecordedScenarioStamp.VersionOf"/> is THE reader of "which version of its scenario did this recording run":
    /// the go-live stamp of the recording's copy (the master's ledger snapshot, else the archive), the meta's load-time hash ONLY as the fallback.
    /// 🔴 Mutations: <c>VersionOf</c> returning <c>meta.ScenarioHash</c> always ⇒ the stamp tests red; the fallback line removed ⇒ the fallback test red;
    /// <c>FindCopy</c> without its ledger arm ⇒ the ledger test red.
    /// </summary>
    public class RecordedScenarioStampTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ce3340-" + Guid.NewGuid().ToString("N"));
        private const string Loaded = """{"name":"s","v":1}""", Saved = """{"name":"s","v":2}""";

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private static RecordingMetadata Meta(Guid id) => new() { ExerciseId = id, ScenarioName = "s", ScenarioHash = ScenarioDescription.HashOf(Loaded) };

        private string Archived(Guid id) => OrchestrationConstants.GetArchivedScenarioDirectory(OrchestrationConstants.GetSharedRoot(_root), id);

        private static void StampFrom(string copy, string scenarioJson)
        {
            Directory.CreateDirectory(copy);
            File.WriteAllText(Path.Combine(copy, "scenario.json"), scenarioJson);
            RecordedScenarioStamp.Write(copy, "s", DateTime.UtcNow);
        }

        [Fact]
        public void TheArchivedCopysStamp_NotTheLoadTimeMetaHash_IsTheVersionThatRan()
        {
            var id = Guid.NewGuid();
            StampFrom(Archived(id), Saved);   // edit → save → go-live

            Assert.Equal(ScenarioDescription.HashOf(Saved), RecordedScenarioStamp.VersionOf(_root, Meta(id)));
            Assert.NotEqual(Meta(id).ScenarioHash, RecordedScenarioStamp.VersionOf(_root, Meta(id)));
        }

        [Fact]
        public void TheMastersLedgerSnapshot_OfAnUnarchivedRun_IsFoundToo_AndWinsOverTheArchive()
        {
            var id = Guid.NewGuid();
            var ledger = Path.Combine(OrchestrationConstants.GetNodeStagingRoot(_root, 7), OrchestrationConstants.RecordingLedgerDirectoryName,
                                      OrchestrationConstants.GetScenarioSnapshotName(id));
            StampFrom(ledger, Saved);
            StampFrom(Archived(id), Loaded);

            Assert.Equal(ledger, RecordedScenarioStamp.FindCopy(_root, id));
            Assert.Equal(ScenarioDescription.HashOf(Saved), RecordedScenarioStamp.VersionOf(_root, Meta(id)));
        }

        [Fact]
        public void ARecordingWithNoCopy_OrNoStamp_OrAnEmptyStampHash_FallsBackToItsMetaHash()
        {
            var none = Guid.NewGuid();
            Assert.Equal(Meta(none).ScenarioHash, RecordedScenarioStamp.VersionOf(_root, Meta(none)));          // no copy at all

            var noStamp = Guid.NewGuid();                                                                          // a copy from before CE-4091: files, no stamp
            Directory.CreateDirectory(Archived(noStamp));
            File.WriteAllText(Path.Combine(Archived(noStamp), "scenario.json"), Saved);
            Assert.Equal(Meta(noStamp).ScenarioHash, RecordedScenarioStamp.VersionOf(_root, Meta(noStamp)));

            var emptyHash = Guid.NewGuid();                                                                        // the copy had no scenario.json ⇒ stamp hash ""
            Directory.CreateDirectory(Archived(emptyHash));
            RecordedScenarioStamp.Write(Archived(emptyHash), "s", DateTime.UtcNow);
            Assert.Equal(Meta(emptyHash).ScenarioHash, RecordedScenarioStamp.VersionOf(_root, Meta(emptyHash)));

            Assert.Equal(Meta(Guid.Empty).ScenarioHash, RecordedScenarioStamp.VersionOf(_root, Meta(Guid.Empty)));  // a recording with no exercise id
        }
    }
}
