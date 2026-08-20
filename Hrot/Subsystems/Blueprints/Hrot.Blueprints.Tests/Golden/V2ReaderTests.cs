using System.Text.Json.Nodes;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Golden;

/// <summary>
/// ⭐⭐ <b><c>U-10</c> — reader, then registry, then writer. All three have landed.</b>
///
/// <para>
/// ⭐ <b>Reader before writer was never a half-measure, it was the safe order.</b> A v2 file is
/// unreadable by any build predating <c>Deserialize</c>'s v2 arm, so readers had to ship first
/// (Batch 54), then a real migrator on the registry (Batch 55 step 2), then the writer (step 3).
/// ⛔ Only the last is irreversible — a migrated file stays migrated, and the down-migrator, not
/// <c>git revert</c>, is what undoes it for anything outside this repo.
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
            // ⭐ Since step 3 the file on disk IS v2; the v1 form is the derived one.
            var onDisk = Load(file);
            var fromV2 = BlueprintJsonServices.Deserialize(onDisk.ToJsonString());
            var fromV1 = BlueprintJsonServices.Deserialize(
                BlueprintSchemaV2.Down(onDisk).ToJsonString());

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

        // ⭐⭐⭐ Batch 86 — RESTATED, and this is now a LOAD-ORDER rail for the retired tag. R-01 makes
        //   WorkingState and Variables one run, so both properties project the whole of it — ⛔ the
        //   claim is no longer "each into its own list" for the state pair, it is that the two v1
        //   groups CONCATENATE in on-disk order, W before V. 🔴 That is R-24: swap them and every
        //   following field's offset moves, hard-resetting live state.
        Assert.Equal(new[] { "W", "V" }, asset.WorkingState.Select(w => w.Name));
        Assert.Equal(new[] { "W", "V" }, asset.Variables.Select(v => v.Name));

        // ⭐ And into the store in KindOrder — the struct layout order, via U-12's grouping invariant.
        Assert.Equal(new[] { "P", "W", "V" }, asset.Declarations.Select(d => d.Name));
        Assert.Equal(
            new[] { DeclarationKind.Parameter, DeclarationKind.Variable, DeclarationKind.Variable },
            asset.Declarations.Select(d => d.Kind));
    }

    /// <summary>
    /// ⭐⭐ <b>Batch 55 step 3 — the writer now emits v2. This test is <c>TheWriterStillEmitsV1</c>,
    /// INVERTED rather than deleted.</b>
    ///
    /// <para>
    /// ⛔ <b>Deleting it would have erased the record that Batch 54's stop was deliberate.</b> That
    /// test existed to make a *decision not to bump* auditable — it asserted `Serialize` emitted v1
    /// and `$meta.schemaVersion` was 1, so the moment anyone flipped the writer it reddened and they
    /// had to say so. ⭐ It duly reddened, here, on purpose. Flipping it in the same commit leaves the
    /// history reading as a decision instead of a disappearance.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>Both halves of "v2" are asserted:</b> the tagged body <b>and</b> the stamp. A document
    /// carrying one without the other is the inconsistency this whole sequence exists to avoid.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWriterNowEmitsV2()
    {
        var asset = BlueprintJsonServices.Deserialize(File.ReadAllText(Files().First()))!;
        var json  = BlueprintJsonServices.Serialize(asset);
        var dom   = JsonNode.Parse(json)!.AsObject();

        Assert.True(BlueprintSchemaV2.IsV2(dom),
            "Serialize no longer emits the v1 three-list shape, but it did not emit the v2 tagged "
            + "array either.");
        Assert.Equal(BlueprintSchemaV2.V2, dom["$meta"]!["schemaVersion"]!.GetValue<int>());

        // ⛔ And the three v1 lists are GONE, not merely accompanied.
        foreach (var list in new[] { "Parameters", "WorkingState", "Variables" })
            Assert.False(dom.ContainsKey(list),
                $"the v2 document still carries the v1 list '{list}' — every declaration would be "
                + "written twice and read back doubled.");
    }

    /// <summary>
    /// ⭐⭐ <b>Batch 55 step 2 — the registry is AHEAD of the writer, deliberately, and this test says
    /// so rather than being deleted.</b>
    ///
    /// <para>
    /// It first asserted the two numbers were equal at <b>1</b>; step 2 opened a deliberate gap by
    /// moving the registry to <b>2</b> ahead of the writer, and step 3 closed it by moving the writer
    /// up to meet it. ⭐ <b>The assertion is stated as an INEQUALITY on purpose</b> — it held through
    /// the window and holds now, so it never had to be weakened and then re-tightened.
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
