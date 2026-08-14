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
    /// ⭐⭐ <b>Batch 55 step 2 — the registry is AHEAD of the writer, deliberately, and this test says
    /// so rather than being deleted.</b>
    ///
    /// <para>
    /// It previously asserted the two numbers were equal at <b>1</b>. Step 2 moves
    /// <c>BlueprintMigrationModule.CurrentVersion</c> to <b>2</b> while <see cref="Serialize"/> still
    /// stamps 1, and that gap is <b>the ordering working</b>, not a defect: a real 1→2 migrator has to
    /// be registered <i>before</i> anything writes v2, or the bump would land with nothing able to
    /// migrate what is already on disk.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>The window is safe in the direction it opens.</b> Disk is <b>behind</b> the registry, so a
    /// file reaches <c>PersistentMigrationAdapter</c>'s <b>up</b> path and is migrated losslessly.
    /// ⛔ The dangerous direction is the reverse — disk ahead of the registry — which is Case D, and
    /// which is exactly what step 3 must not create: it closes this gap by moving the writer up to
    /// meet the registry, never by moving the registry down.
    /// </para>
    ///
    /// <para>
    /// 📌 <b>One live consequence of step 2 on its own:</b> <c>--mode migrate</c> now genuinely
    /// rewrites blueprints to v2 on disk, before <see cref="Serialize"/> ever emits it. That is the
    /// tool doing its job, and the reader has understood v2 since Batch 54.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRegistryIsAheadOfTheWriterAndNeverBehindIt()
    {
        var registry = Hrot.Common.Scenario.Migrations.BlueprintMigrationModule.CurrentVersion;
        var stamped  = StampedVersion();

        Assert.Equal(BlueprintSchemaV2.V2, registry);
        Assert.True(stamped <= registry,
            $"the writer stamps v{stamped} while the migration registry is at v{registry}. A disk "
            + "version AHEAD of the registry reaches PersistentMigrationAdapter's Case D, which throws "
            + "when there is no down-chain and no snapshot.");
    }

    private static int StampedVersion()
    {
        var asset = BlueprintJsonServices.Deserialize(File.ReadAllText(Files().First()))!;
        var dom   = JsonNode.Parse(BlueprintJsonServices.Serialize(asset))!.AsObject();
        return dom["$meta"]!["schemaVersion"]!.GetValue<int>();
    }
}
