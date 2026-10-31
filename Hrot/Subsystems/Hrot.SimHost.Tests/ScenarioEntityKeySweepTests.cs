using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Testing;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4153</c> acceptance (docs/DESIGN_Scenario_Entity_Keys.md §4) — every scenario the repo ships loads and saves back under
    /// the SAME keys: the entity key is the entity's stable <see cref="EntityGuid"/>, so load → save repeats the file's keys, and a
    /// second save of the unchanged world is byte-identical (before CE-4153 every save re-keyed every entity).
    /// <para>Runs on a repo registered the way every Hrot host registers it (<c>HrotSharedComponentRegistry</c>) with the real translator set
    /// and <see cref="UnknownComponentPolicy.WarnAndSkip"/>: the point is the KEYS and the entity set, which are what changed.</para>
    /// </summary>
    public sealed class ScenarioEntityKeySweepTests
    {
        private static IEnumerable<string> ScenarioFiles()
        {
            string root = TestRepo.Root;
            foreach (var dir in new[] { "scenarios", Path.Combine("recipes", "scenarios") })
            {
                var full = Path.Combine(root, dir);
                if (!Directory.Exists(full)) continue;
                foreach (var f in Directory.EnumerateFiles(full, "scenario.json", SearchOption.AllDirectories)) yield return f;
            }
        }

        [Fact]
        public void EveryShippedScenario_LoadsAndSavesUnderItsOwnKeys_AndTheSecondSaveIsIdentical()
        {
            var files = ScenarioFiles().ToList();
            Assert.True(files.Count >= 100, $"expected the repo's scenarios, found {files.Count}");

            var failures = new List<string>();
            int entitiesChecked = 0;
            var skipped = new List<string>();
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var inDom = JsonNode.Parse(json)!.AsObject();
                    if ((inDom["entities"] ?? inDom["Entities"]) is not JsonObject inEntities) { skipped.Add(Path.GetRelativePath(TestRepo.Root, file)); continue; }   // not an entity-keyed scenario
                    var inKeys = inEntities.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();

                    var serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(
                        new Fdp.Toolkit.Behavior.BehaviorRegistry(), null, UnknownComponentPolicy.WarnAndSkip);
                    using var repo = new EntityRepository();
                    HrotSharedComponentRegistry.RegisterAll(repo);
                    Hrot.CGF.CgfComponentRegistry.RegisterAll(repo);   // the CGF host's own set: the load-time intents and the mission queue
                    serializer.Deserialize(repo, json);

                    var first  = serializer.Serialize(repo, new ScenarioHeader(Hrot.Common.Scenario.HrotSubsystemTypes.Scenario));
                    var second = serializer.Serialize(repo, new ScenarioHeader(Hrot.Common.Scenario.HrotSubsystemTypes.Scenario));
                    var outKeys = first["Entities"]!.AsObject().Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();

                    if (!inKeys.SequenceEqual(outKeys))
                        failures.Add($"{Path.GetRelativePath(TestRepo.Root, file)}: keys changed ({inKeys.Length} in, {outKeys.Length} out; first differing: {inKeys.Except(outKeys).FirstOrDefault() ?? "-"})");
                    else if (first.ToJsonString() != second.ToJsonString())
                        failures.Add($"{Path.GetRelativePath(TestRepo.Root, file)}: two saves of the unchanged world differ");
                    entitiesChecked += inKeys.Length;
                }
                catch (Exception ex)
                {
                    failures.Add($"{Path.GetRelativePath(TestRepo.Root, file)}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Assert.True(failures.Count == 0, $"{failures.Count}/{files.Count} scenarios:\n" + string.Join("\n", failures));
            Assert.True(entitiesChecked >= 1000, $"only {entitiesChecked} entities swept");
        }
    }
}
