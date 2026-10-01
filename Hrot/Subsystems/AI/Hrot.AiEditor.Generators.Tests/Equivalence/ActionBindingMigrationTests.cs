using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using FluentAssertions;
using Hrot.AiEditor.Generators.Tests.Golden;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Equivalence;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-417</c> — the v1 → v2 migrator (<see cref="ActionBindingMigrator"/>) loses nothing.</b>
/// 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §4 B-4, §6 ("the migrator round-trips").
///
/// <para>⭐ <b>The fixtures are the REAL pre-migration files</b> (<c>Snapshots/MigrationV1/*.v1.json</c>, taken from the
/// commit before the corpus was rewritten). A v1 file and its rewritten v2 corpus file must load into the SAME DTO —
/// compared through <c>Serialize</c>, so every field counts. ⇒ the emitted source is identical by construction, which the
/// unchanged emitted-source goldens (<c>HsmGoldenCorpusTests</c> / <c>BTreeGoldenCorpusTests</c>) confirm separately.</para>
/// </summary>
public sealed class ActionBindingMigrationTests
{
    private static string V1Dir => Path.Combine(AiGoldenSnapshot.ResolveSnapshotsDir(), "MigrationV1");

    [Theory]
    [InlineData("HsmChannelE2E")]
    [InlineData("HsmPolledGuardDemo")]
    [InlineData("HsmShowcase")]
    [InlineData("HsmVariableShowcase")]
    public void AV1HsmFile_LoadsIntoTheSameDtoAsItsV2CorpusFile(string name)
    {
        string v1 = File.ReadAllText(Path.Combine(V1Dir, name + ".hsm.v1.json"));
        string v2 = AiAssetCorpus.ReadAsset(AiAssetKind.Hsm, name);
        v1.Should().Contain("\"schemaVersion\": 1", "the fixture must really be a v1 file");

        HsmJsonServices.Serialize(HsmJsonServices.Deserialize(v1)!)
            .Should().Be(HsmJsonServices.Serialize(HsmJsonServices.Deserialize(v2)!));
    }

    [Theory]
    [InlineData("PlatoonHillAttack")]
    [InlineData("T35_SharedWorkingState")]
    [InlineData("T20_MultiStateful")]
    [InlineData("T34_ComposedAiPrimitiveCondition")]
    public void AV1BTreeFile_LoadsIntoTheSameDtoAsItsV2CorpusFile(string name)
    {
        string v1 = File.ReadAllText(Path.Combine(V1Dir, name + ".btree.v1.json"));
        string v2 = AiAssetCorpus.ReadAsset(AiAssetKind.BTree, name);
        v1.Should().Contain("\"schemaVersion\": 1", "the fixture must really be a v1 file");

        BTreeJsonServices.Serialize(BTreeJsonServices.Deserialize(v1)!)
            .Should().Be(BTreeJsonServices.Serialize(BTreeJsonServices.Deserialize(v2)!));
    }

    // ── the rules, one each ─────────────────────────────────────────────────────────────

    [Fact]
    public void BTree_DelegateShapeMovesToTheNode_AndThePayloadIsTheBinding()
    {
        var dto = BTreeJsonServices.Deserialize("""
            { "$meta": { "schemaVersion": 1 }, "Nodes": [
              { "kind": "Action", "VisualId": "00000000-0000-0000-0000-000000000001",
                "Action": { "MethodFqn": "N.C.M", "ExpressionTargetField": "v", "DelegateShape": "ThreeParamReusableStateful",
                            "WorkingStateTypeId": "N.WS" } },
              { "kind": "Condition", "VisualId": "00000000-0000-0000-0000-000000000002",
                "Condition": { "MethodFqn": "", "DelegateShape": "AiPrimitiveTickCore" } } ] }
            """)!;

        var act = dto.Nodes.OfType<BTreeActionNodeDto>().Single();
        act.DelegateShape.Should().Be(BTreeDelegateShapeDto.ThreeParamReusableStateful);
        act.Action!.MethodFqn.Should().Be("N.C.M");
        act.Action.ExpressionTargetField.Should().Be("v");
        act.Action.WorkingStateTypeId.Should().Be("N.WS");

        var cond = dto.Nodes.OfType<BTreeConditionNodeDto>().Single();
        cond.DelegateShape.Should().Be(BTreeDelegateShapeDto.AiPrimitiveTickCore);
        cond.Condition!.MethodFqn.Should().BeNull("v1 wrote an absent method as \"\"; v2 omits it");
    }

    [Fact]
    public void Hsm_TheStateFieldGoesToEverySlotThatIsSet()
    {
        var st = HsmJsonServices.Deserialize("""
            { "$meta": { "schemaVersion": 1 }, "States": [ { "StableId": "00000000-0000-0000-0000-00000000000a",
              "OnEntryAction": "N.C.Enter", "TimerAction": "N.C.Tick",
              "ActivityBlueprintAssetId": "00000150-0000-0000-0000-000000000001", "ActivityBlueprintName": "Bp",
              "ExpressionTargetField": "Seed" } ] }
            """)!.States.Single();

        st.OnEntry!.MethodFqn.Should().Be("N.C.Enter");
        st.Timer!.MethodFqn.Should().Be("N.C.Tick");
        st.OnExit.Should().BeNull();
        st.Activity!.BlueprintAssetId.Should().Be(new Guid("00000150-0000-0000-0000-000000000001"));
        st.Activity.BlueprintName.Should().Be("Bp");
        new[] { st.OnEntry, st.Timer, st.Activity }.Should().OnlyContain(b => b.ExpressionTargetField == "Seed",
            "v1's one state field was shared by all four slots (B-2)");
    }

    [Fact]
    public void Hsm_AFieldBoundBeforeAnyActionWasChosen_IsKeptOnAnActivityThatNamesNothing()
    {
        var st = HsmJsonServices.Deserialize("""
            { "$meta": { "schemaVersion": 1 }, "States": [ { "StableId": "00000000-0000-0000-0000-00000000000a",
              "ExpressionTargetField": "EngageTarget" } ] }
            """)!.States.Single();

        st.Activity.Should().NotBeNull("the field must survive — it feeds the state-wide seed lookup");
        st.Activity!.IsEmpty.Should().BeTrue();
        st.Activity.ExpressionTargetField.Should().Be("EngageTarget");
    }

    [Fact]
    public void Hsm_ATransitionsFieldGoesToBothItsGuardAndItsAction()
    {
        var dto = HsmJsonServices.Deserialize("""
            { "$meta": { "schemaVersion": 1 },
              "Transitions": [ { "GuardBlueprintAssetId": "00000150-0000-0000-0000-000000000001", "GuardBlueprintName": "G",
                                 "ActionFunction": "N.C.Do", "ExpressionTargetField": "T" } ],
              "GlobalTransitions": [ { "GuardFunction": "N.C.Ok", "ExpressionTargetField": "U" } ] }
            """)!;

        var tr = dto.Transitions.Single();
        tr.Guard!.BlueprintAssetId.Should().Be(new Guid("00000150-0000-0000-0000-000000000001"));
        tr.Guard.BlueprintName.Should().Be("G");
        tr.Guard.ExpressionTargetField.Should().Be("T");
        tr.Action!.MethodFqn.Should().Be("N.C.Do");
        tr.Action.ExpressionTargetField.Should().Be("T");

        var gt = dto.GlobalTransitions.Single();
        gt.Guard!.MethodFqn.Should().Be("N.C.Ok");
        gt.Guard.ExpressionTargetField.Should().Be("U");
        gt.Action.Should().BeNull();
    }

    [Fact]
    public void AV2Document_IsLeftAlone()
    {
        var root = JsonNode.Parse("""
            { "$meta": { "schemaVersion": 2 }, "States": [ { "ExpressionTargetField": "stays-put" } ] }
            """)!.AsObject();
        ActionBindingMigrator.UpgradeHsm(root);
        root["States"]![0]!["ExpressionTargetField"]!.GetValue<string>().Should().Be("stays-put",
            "a v2 document is never re-migrated");
    }
}
