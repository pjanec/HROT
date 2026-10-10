using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.Emit;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-504</c> slice 1 (C-1) — a node's call shape is DERIVED from the method it binds.</b>
/// 📄 <c>docs/blueprints/DESIGN_BTree_Node_Call_Shapes.md</c> §4 C-1, §5.
/// </summary>
public sealed class BTreeCallShapeTests
{
    /// <summary>
    /// ⭐⭐⭐ <b>The equivalence that made dropping the persisted field safe.</b> Every corpus binding, classified from its
    /// method's REAL signature (reflection over <c>Hrot.AI.Behaviors</c>), gets the shape the file used to record. ⭐ The
    /// table below was captured from the files' own <c>DelegateShape</c> values (2026-10-02, before the field was removed),
    /// by a script independent of the classifier — so a classifier change that would alter emitted code fails here.
    /// <para>✅ Red-proof: swap the stateful and plain three-param arms in <c>FromSignature</c> ⇒ this rail reddens.</para>
    /// </summary>
    [Fact]
    public void EveryCorpusBinding_ClassifiesToTheShapeItsFileRecorded()
    {
        var signatures = BTreeCallShapes.ReflectionSignatures(new[] { typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly });
        var actual = new List<(string File, string VisualId, BTreeDelegateShapeDto Shape)>();
        foreach (var path in Directory.GetFiles(CorpusDir(), "*.btree.json", SearchOption.AllDirectories)
                                      .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
        {
            var dto = BTreeJsonServices.Deserialize(File.ReadAllText(path))!;
            BTreeCallShapes.Apply(dto, signatures);
            foreach (var node in dto.Nodes)
            {
                if (node is BTreeActionNodeDto { Action: not null } a)
                    actual.Add((Path.GetFileName(path), a.VisualId.ToString("D"), a.DelegateShape));
                else if (node is BTreeConditionNodeDto { Condition: not null } c)
                    actual.Add((Path.GetFileName(path), c.VisualId.ToString("D"), c.DelegateShape));
            }
        }

        actual.Should().BeEquivalentTo(Recorded,
            "the derived shape must equal the one each corpus file recorded, or the emitted code changes");
    }

    [Fact]
    public void ABlueprintBinding_IsTheBlueprintCall_WithoutASignature()
        => BTreeCallShapes.Classify(
               new BehaviorActionBindingDto { BlueprintAssetId = Guid.NewGuid(), BlueprintName = "X" }, _ => null)
           .Should().Be(BTreeDelegateShapeDto.AiPrimitiveTickCore);

    [Fact]
    public void AnUnresolvableMethod_HasNoShape()
        => BTreeCallShapes.Classify(new BehaviorActionBindingDto { MethodFqn = "No.Such.Method" }, _ => null)
           .Should().BeNull("the validator reports it (BTREE0002) rather than a shape being guessed");

    [Fact]
    public void ASharedAiMethod_IsThePlainThreeParamShape()
        => BTreeCallShapes.FromSignature(new[]
           {
               new CallParam("Demo.P", true), new CallParam("Fdp.Core.Entity", false),
               new CallParam("Fdp.Core.EntityRepository", false),
           }).Should().Be(BTreeDelegateShapeDto.Plain);

    private static string CorpusDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", "BTrees");
            if (Directory.Exists(c)) return c;
        }
        throw new DirectoryNotFoundException("BTree corpus not found above " + AppContext.BaseDirectory);
    }

    // ⭐ CE-504 slice 3 — the seven `CgfNodes.Action_Wander` rows were recorded `FourParamFull`; C-3 moved Wander to the
    //   param-less shared form `(Entity, EntityRepository)`, so they now derive `NoParams`.
    private static readonly (string File, string VisualId, BTreeDelegateShapeDto Shape)[] Recorded =
    {
        ("BTreeCuratedBindingDemo.btree.json", "bb000417-0000-0000-0000-000000000003", BTreeDelegateShapeDto.Plain),
        ("BTreeCuratedBindingDemo.btree.json", "bb000417-0000-0000-0000-000000000004", BTreeDelegateShapeDto.Plain),
        ("T04_DecoratorRepeater.btree.json", "b5030000-0000-0000-0000-000000000001", BTreeDelegateShapeDto.NoParams),
        ("T05_DecoratorStack.btree.json", "b6030000-0000-0000-0000-000000000001", BTreeDelegateShapeDto.NoParams),
        ("T06_ObserverSelector.btree.json", "b7030000-0000-0000-0000-000000000001", BTreeDelegateShapeDto.NoParams),
        ("T08_ActionLeaf.btree.json", "b9020000-0000-0000-0000-000000000001", BTreeDelegateShapeDto.NoParams),
        ("T10_MultiAction.btree.json", "ba030000-0000-0000-0000-000000000001", BTreeDelegateShapeDto.Plain),
        ("T10_MultiAction.btree.json", "ba040000-0000-0000-0000-000000000001", BTreeDelegateShapeDto.Plain),
        ("T10_MultiAction.btree.json", "ba050000-0000-0000-0000-000000000001", BTreeDelegateShapeDto.Plain),
        ("T11_Aliasing.btree.json", "bb030000-0000-0000-0000-000000000001", BTreeDelegateShapeDto.Plain),
        ("T11_Aliasing.btree.json", "bb040000-0000-0000-0000-000000000001", BTreeDelegateShapeDto.Plain),
        ("T20_MultiStateful.btree.json", "bb200000-0000-0000-0000-000000000003", BTreeDelegateShapeDto.Stateful),
        ("T20_MultiStateful.btree.json", "bb200000-0000-0000-0000-000000000004", BTreeDelegateShapeDto.Stateful),
        ("T20_MultiStateful.btree.json", "bb200000-0000-0000-0000-000000000005", BTreeDelegateShapeDto.Plain),
        ("T31_ComposedAiPrimitive.btree.json", "bb310000-0000-0000-0000-000000000002", BTreeDelegateShapeDto.AiPrimitiveTickCore),
        ("T32_ComposedGeneratedBlueprint.btree.json", "bb320000-0000-0000-0000-000000000002", BTreeDelegateShapeDto.AiPrimitiveTickCore),
        ("T33_ComposedParamBlueprint.btree.json", "bb330000-0000-0000-0000-000000000011", BTreeDelegateShapeDto.AiPrimitiveTickCore),
        ("T34_ComposedAiPrimitiveCondition.btree.json", "bb340000-0000-0000-0000-000000000002", BTreeDelegateShapeDto.AiPrimitiveTickCore),
        ("T35_SharedWorkingState.btree.json", "bb350000-0000-0000-0000-000000000003", BTreeDelegateShapeDto.AiPrimitiveTickCore),
        ("T35_SharedWorkingState.btree.json", "bb350000-0000-0000-0000-000000000004", BTreeDelegateShapeDto.AiPrimitiveTickCore),
        ("T35_SharedWorkingState.btree.json", "bb350000-0000-0000-0000-000000000005", BTreeDelegateShapeDto.AiPrimitiveTickCore),
        ("T39_TwoDistinctPrimitives.btree.json", "bb390000-0000-0000-0000-000000000003", BTreeDelegateShapeDto.AiPrimitiveTickCore),
        ("T39_TwoDistinctPrimitives.btree.json", "bb390000-0000-0000-0000-000000000004", BTreeDelegateShapeDto.AiPrimitiveTickCore),
        ("T39_TwoDistinctPrimitives.btree.json", "bb390000-0000-0000-0000-000000000005", BTreeDelegateShapeDto.AiPrimitiveTickCore),
        ("BTreeRenderShowcase.btree.json", "bb060000-0000-0000-0000-000000000001", BTreeDelegateShapeDto.NoParams),
        ("BTreeRenderShowcase.btree.json", "bb080000-0000-0000-0000-000000000001", BTreeDelegateShapeDto.NoParams),
        ("CombatShowcase.btree.json", "50000000-0000-0000-0000-000000000001", BTreeDelegateShapeDto.NoParams),
        // ⭐ CE-2080 — the shipped SOP: its four SOP-order rows (DoWhenIdle / React) are plain actions.
        ("TakeCover.btree.json", "c2092000-0000-0000-0000-000000000002", BTreeDelegateShapeDto.Stateful),   // CE-2092
        ("FallBack.btree.json", "c2093000-0000-0000-0000-000000000002", BTreeDelegateShapeDto.Stateful),    // CE-2093
        // ⭐ CE-2073 — CombatPosture: derived, not guessed: ChooseOption and the four IsOption conditions classify Plain, the posture / EQS actions Stateful, Hold NoParams.
        ("CombatPosture.btree.json", "c2073000-0000-0000-0000-000000000003", BTreeDelegateShapeDto.Plain),
        ("CombatPosture.btree.json", "c2073000-0000-0000-0000-000000000004", BTreeDelegateShapeDto.Stateful),
        ("CombatPosture.btree.json", "c2073000-0000-0000-0000-000000000007", BTreeDelegateShapeDto.Plain),
        ("CombatPosture.btree.json", "c2073000-0000-0000-0000-000000000008", BTreeDelegateShapeDto.Stateful),
        ("CombatPosture.btree.json", "c2073000-0000-0000-0000-000000000010", BTreeDelegateShapeDto.Plain),
        ("CombatPosture.btree.json", "c2073000-0000-0000-0000-000000000011", BTreeDelegateShapeDto.Stateful),
        ("CombatPosture.btree.json", "c2073000-0000-0000-0000-000000000013", BTreeDelegateShapeDto.Plain),
        ("CombatPosture.btree.json", "c2073000-0000-0000-0000-000000000014", BTreeDelegateShapeDto.Stateful),
        ("CombatPosture.btree.json", "c2073000-0000-0000-0000-000000000016", BTreeDelegateShapeDto.Plain),
        ("CombatPosture.btree.json", "c2073000-0000-0000-0000-000000000017", BTreeDelegateShapeDto.Stateful),
        ("CombatPosture.btree.json", "c2073000-0000-0000-0000-000000000018", BTreeDelegateShapeDto.NoParams),
        ("CombatPosture.btree.json", "c3084000-0000-0000-0000-000000000002", BTreeDelegateShapeDto.Plain),    // CE-3084 G6: ChooseOption(approach)
        ("CombatPosture.btree.json", "c3084000-0000-0000-0000-000000000003", BTreeDelegateShapeDto.Stateful),  // ApproachSensors
        ("CombatPosture.btree.json", "c3084000-0000-0000-0000-000000000006", BTreeDelegateShapeDto.Plain),     // IsOption(isFlank)
        ("CombatPosture.btree.json", "c3084000-0000-0000-0000-000000000009", BTreeDelegateShapeDto.Plain),     // IsOption(isFiringPos)
        ("CombatPosture.btree.json", "c3084000-0000-0000-0000-000000000011", BTreeDelegateShapeDto.Stateful),
        ("CombatPosture.btree.json", "c3084000-0000-0000-0000-000000000012", BTreeDelegateShapeDto.Stateful),
        ("CombatPosture.btree.json", "c3090000-0000-0000-0000-000000000002", BTreeDelegateShapeDto.Plain),       // CE-3090 is hold prone
        ("CombatPosture.btree.json", "c3090000-0000-0000-0000-000000000003", BTreeDelegateShapeDto.Stateful),    // CE-3090 HoldProne
        ("Flank.btree.json", "c2108000-0000-0000-0000-000000000003", BTreeDelegateShapeDto.Stateful),            // CE-2108
        ("Flank.btree.json", "c2108000-0000-0000-0000-000000000004", BTreeDelegateShapeDto.Stateful),
        ("FiringPosition.btree.json", "c2108000-0000-0000-0000-000000000103", BTreeDelegateShapeDto.Stateful),
        ("FiringPosition.btree.json", "c2108000-0000-0000-0000-000000000104", BTreeDelegateShapeDto.Stateful),
        ("Sentry.btree.json", "c3079000-0000-0000-0000-000000000004", BTreeDelegateShapeDto.Plain),              // CE-3079 H2
        ("DangerCrossing.btree.json", "c3079100-0000-0000-0000-000000000003", BTreeDelegateShapeDto.Stateful),    // CE-3079 H5
        ("DangerCrossing.btree.json", "c3079100-0000-0000-0000-000000000006", BTreeDelegateShapeDto.Plain),
        ("DangerCrossing.btree.json", "c3079100-0000-0000-0000-000000000007", BTreeDelegateShapeDto.Stateful),
        ("DangerCrossing.btree.json", "c3079100-0000-0000-0000-000000000009", BTreeDelegateShapeDto.Plain),
        ("DangerCrossing.btree.json", "c3079100-0000-0000-0000-000000000010", BTreeDelegateShapeDto.Stateful),
        ("DangerCrossing.btree.json", "c3079100-0000-0000-0000-000000000011", BTreeDelegateShapeDto.Plain),
        ("PostureAdvance.btree.json", "c3083100-0000-0000-0000-000000000002", BTreeDelegateShapeDto.Stateful),    // CE-3083 G5 wrappers
        ("PostureSuppress.btree.json", "c3083200-0000-0000-0000-000000000002", BTreeDelegateShapeDto.Stateful),
        ("PostureHold.btree.json", "c3083300-0000-0000-0000-000000000002", BTreeDelegateShapeDto.NoParams),
        ("PostureSense.btree.json", "c3083400-0000-0000-0000-000000000002", BTreeDelegateShapeDto.Stateful),
        ("PostureHoldProne.btree.json", "c3090200-0000-0000-0000-000000000002", BTreeDelegateShapeDto.Stateful), // CE-3090
        ("BasicInfantrySop.btree.json", "c2080000-0000-0000-0000-000000000011", BTreeDelegateShapeDto.Plain),
        ("BasicInfantrySop.btree.json", "c2080000-0000-0000-0000-000000000021", BTreeDelegateShapeDto.Plain),
        ("BasicInfantrySop.btree.json", "c2080000-0000-0000-0000-000000000022", BTreeDelegateShapeDto.Plain),
        ("BasicInfantrySop.btree.json", "c2080000-0000-0000-0000-000000000031", BTreeDelegateShapeDto.Plain),
        ("HullDownAttackRun.btree.json", "2b000000-0000-0000-0000-0000000000a1", BTreeDelegateShapeDto.Plain),
        ("HullDownAttackRun.btree.json", "2b000000-0000-0000-0000-0000000000a2", BTreeDelegateShapeDto.Plain),
        ("HullDownAttackRun.btree.json", "2b000000-0000-0000-0000-0000000000a3", BTreeDelegateShapeDto.Plain),
        ("HullDownAttackRun.btree.json", "2b000000-0000-0000-0000-0000000000a4", BTreeDelegateShapeDto.Plain),
        ("PlatoonHillAttack.btree.json", "1a000000-0000-0000-0000-0000000000a1", BTreeDelegateShapeDto.Stateful),
        ("PlatoonHillAttack.btree.json", "1a000000-0000-0000-0000-0000000000a2", BTreeDelegateShapeDto.Stateful),
        ("PlatoonHillAttack.btree.json", "1a000000-0000-0000-0000-0000000000a3", BTreeDelegateShapeDto.Plain),
        ("PlatoonHillAttack.btree.json", "1a000000-0000-0000-0000-0000000000b1", BTreeDelegateShapeDto.Stateful),
        ("PlatoonHillAttack.btree.json", "1a000000-0000-0000-0000-0000000000b2", BTreeDelegateShapeDto.Stateful),
        ("PlatoonHillAttack.btree.json", "1a000000-0000-0000-0000-0000000000b3", BTreeDelegateShapeDto.Stateful),
        ("PlatoonHillAttack.btree.json", "1a000000-0000-0000-0000-0000000000b4", BTreeDelegateShapeDto.Stateful),
        ("PlatoonHillAttack.btree.json", "1a000000-0000-0000-0000-0000000000a4", BTreeDelegateShapeDto.Stateful),
        ("PlatoonHillAttack.btree.json", "1a000000-0000-0000-0000-0000000000a5", BTreeDelegateShapeDto.Plain),
    };
}
