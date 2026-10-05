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
