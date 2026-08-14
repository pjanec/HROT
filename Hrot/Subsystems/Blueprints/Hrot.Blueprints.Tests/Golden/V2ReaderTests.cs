using System.Text.Json.Nodes;
using Hrot.Blueprints.Core;

namespace Hrot.Blueprints.Tests.Golden;

/// <summary>
/// ⭐⭐ <b><c>U-10</c> — the reader understands v2, and nothing writes it yet.</b>
///
/// <para>
/// ⭐ <b>Reader before writer is not a half-measure, it is the safe order.</b> A v2 file is unreadable
/// by any build that predates <c>Deserialize</c>'s v2 arm, so every reader has to ship first. ⇒ this
/// half is reversible by <c>git revert</c>; ⛔ the writer bump is not, because a migrated file stays
/// migrated.
/// </para>
/// </summary>
public sealed class V2ReaderTests
{
    private static IEnumerable<string> Files() => CorpusCanonicalisationTests.AllManagedFiles();

    private static JsonObject Load(string file) => JsonNode.Parse(File.ReadAllText(file))!.AsObject();

    /// <summary>
    /// ⭐⭐ <b>Pass 2, over all 58: the v2 form of every shipped asset loads into the same model the v1
    /// form does.</b> ⚠ Compared by re-serializing both through the canonical writer — a field-by-field
    /// comparison would need a comparer that could itself be wrong about which fields exist.
    /// </summary>
    [Fact]
    public void EveryShippedAssetLoadsIdenticallyFromItsV2Form()
    {
        var broken = new List<string>();

        foreach (var file in Files())
        {
            var text = File.ReadAllText(file);
            var fromV1 = BlueprintJsonServices.Deserialize(text);
            var fromV2 = BlueprintJsonServices.Deserialize(
                BlueprintSchemaV2.Up(Load(file)).ToJsonString());

            if (fromV1 is null || fromV2 is null)
            {
                broken.Add(Path.GetFileName(file) + " (deserialized null)");
                continue;
            }

            if (!string.Equals(BlueprintJsonServices.Serialize(fromV1),
                               BlueprintJsonServices.Serialize(fromV2), StringComparison.Ordinal))
                broken.Add(Path.GetFileName(file));
        }

        Assert.True(broken.Count == 0,
            "the v2 form does not load into the same model as the v1 form for:\n  "
            + string.Join("\n  ", broken));
    }

    /// <summary>
    /// ⭐ <b>The declarations arrive in their own kinds, not merely in the right total.</b> ⛔ Without
    /// this the previous test would pass on an asset whose kinds were swapped, provided the round trip
    /// swapped them back consistently.
    /// </summary>
    [Fact]
    public void AV2DocumentLoadsEachDeclarationIntoItsOwnKind()
    {
        var v1 = JsonNode.Parse(@"{
  ""Name"": ""KindsFromV2"",
  ""Dispatch"": ""Instance"",
  ""Parameters"": [{ ""Id"": ""11111111-1111-1111-1111-111111111111"", ""Name"": ""P"" }],
  ""ParameterOrder"": null,
  ""WorkingState"": [{ ""Id"": ""22222222-2222-2222-2222-222222222222"", ""Name"": ""W"" }],
  ""WorkingStateOrder"": null,
  ""Variables"": [{ ""Id"": ""33333333-3333-3333-3333-333333333333"", ""Name"": ""V"" }],
  ""VariableOrder"": null
}")!.AsObject();

        var asset = BlueprintJsonServices.Deserialize(
            BlueprintSchemaV2.Up(v1).ToJsonString())!;

        Assert.Equal(new[] { "P" }, asset.Parameters.Select(p => p.Name));
        Assert.Equal(new[] { "W" }, asset.WorkingState.Select(w => w.Name));
        Assert.Equal(new[] { "V" }, asset.Variables.Select(v => v.Name));

        // ⭐ And into the store in KindOrder — the struct layout order, via U-12's grouping invariant.
        Assert.Equal(new[] { "P", "W", "V" }, asset.Declarations.Select(d => d.Name));
    }

    /// <summary>
    /// ⛔⛔ <b>Nothing writes v2 — asserted, not assumed.</b> ⭐ This is the gate that makes the batch's
    /// stop point auditable: the moment <see cref="BlueprintJsonServices.Serialize"/> starts emitting
    /// the v2 shape, or <c>$meta.schemaVersion</c> moves off 1, this reddens and whoever did it has to
    /// say so. See <c>BP-235</c> for why it cannot move yet.
    /// </summary>
    [Fact]
    public void TheWriterStillEmitsV1()
    {
        var asset = BlueprintJsonServices.Deserialize(File.ReadAllText(Files().First()))!;
        var json  = BlueprintJsonServices.Serialize(asset);
        var dom   = JsonNode.Parse(json)!.AsObject();

        Assert.False(BlueprintSchemaV2.IsV2(dom));
        Assert.Equal(BlueprintSchemaV2.V1,
            dom["$meta"]!["schemaVersion"]!.GetValue<int>());
    }

    /// <summary>
    /// ⭐ <b>The two version numbers agree today, and this is what will catch them disagreeing.</b>
    /// ⚠ <c>BlueprintMigrationModule.CurrentVersion</c> lives in <c>Hrot.Common</c> and governs what
    /// <c>ClusterRunner --mode migrate</c> does with a blueprint file; <c>$meta.schemaVersion</c> is
    /// what this assembly stamps. ⛔ If one moves without the other, a disk version newer than the
    /// registry's current version reaches <c>PersistentMigrationAdapter</c>'s Case D — which, with no
    /// down-migration chain registered and no snapshot, <b>throws</b>.
    /// </summary>
    [Fact]
    public void TheStampedVersionAgreesWithTheMigrationRegistry()
    {
        Assert.Equal(Hrot.Common.Scenario.Migrations.BlueprintMigrationModule.CurrentVersion,
                     BlueprintSchemaV2.V1);
    }
}
