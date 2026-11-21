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
        // CE-2322: the toolkit assembly too (SopConditions lives there), as the editor's AppDomain source sees it — an
        //   unresolved method keeps the default Plain, which hid that every SensedFresh row went unresolved.
        var signatures = BTreeCallShapes.ReflectionSignatures(new[]
        {
            typeof(Fdp.Toolkit.Behavior.SopConditions).Assembly,
            typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly,
        });
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
        var c = Fdp.Toolkit.Orchestration.ContentLayout.BehaviorAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!, Fdp.Toolkit.Orchestration.ContentLayout.BTreesFolder);
        return Directory.Exists(c) ? c : throw new DirectoryNotFoundException("BTree corpus not found: " + c);
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
        // ⛔ CE-2073's CombatPosture rows and CE-3083's Posture* wrapper rows went with those assets — CE-3160 (R-266): CombatPosture is C#-built.
        // ⭐ CE-3189 (CE-2123 S2's squad SOP, never recorded) — shapes read from the SOURCE signatures, not the classifier:
        //   SopConditions.SensedFresh (SopConditions.cs:55) and SquadSopConditions.SensedFreshAboard (SquadDismountBehavior.cs:145)
        //   are both (ref SopSenseParams, Entity, EntityRepository) ⇒ the plain three-param shared form, as BasicInfantrySop records.
        // ⭐ CE-2165 V8 — the vehicle SOP's three SensedFresh rows (air threat, hit, first threat): the plain shared form, as above.
        // ⭐ CE-2191 (Air Ops §7f) — the air-defence row of both SOPs: SensedFresh (air threat) and AirDefenceNodes.CanEngageAir
        //   (AirDefenceBehaviors.cs:118, (ref EngageAircraftParams, Entity, EntityRepository)) ⇒ both the plain three-param shared form.
        // ⭐ CE-2307 (Squad Tactics §5b CH-1) — the push-through row: SensedFresh (hit) is plain; ConvoyConditions.CanPushThrough is
        //   (Entity, EntityRepository) ⇒ NoParams.
        // ⭐ CE-2294 CC-D — the platoon's SOP row: SopConditions.SensedFresh and PictureConditions.ElementReported are both
        //   (ref TParams, Entity, EntityRepository) ⇒ the plain three-param shared form.
        // ⭐ CE-2276 DA-55 — the hold row's first guard: PlatoonDefendBehavior.NotDefending is (Entity, EntityRepository) ⇒ NoParams.
        // ⭐ CE-2277 AT-13 — the same row's second guard: PlatoonAttackBehavior.NotAttacking is (Entity, EntityRepository) ⇒ NoParams.
        // ⭐ CE-2314 — and its third: SupportCommandBehavior.NotCommandingSupport is (Entity, EntityRepository) ⇒ NoParams.
        // ⭐ CE-2173 slice 3 (Air Operations §9b) — SquadSopConditions.CarrierUnloading and .Riding are (Entity, EntityRepository) ⇒ NoParams.
        // ⭐ CE-1118 H-1 (Airborne Insertion §6) — SquadSopConditions.CarrierRoping / .CarrierDropping are (Entity, EntityRepository) ⇒ NoParams.
        // ⭐ CE-1118 H-3 (D-23) — the pilot's SOP row: AirborneIndividualNodes.OnTheGround is (Entity, EntityRepository) ⇒ NoParams.
        // ⭐ CE-2322 (AQ97) — the soldier's gates SopConditions.UnderCommand / .Aboard are (Entity, EntityRepository) ⇒ NoParams; the
        //   near-miss row's SensedFresh is plain; the civilian SOP's CivilianBehavior.NotOnHisRoutine / .FrightenedOrHurt are NoParams.
        // ⭐ CE-2331 (AQ98 rows), CE-2347 (the convoy's squad SOP rows), CE-2284 U-2 (the platoon's react-to-fire row) — recorded 2026-10-22
        // ⭐ CE-3306 — the enemy-in-sight row's CoverFightConditions.MayFire · ⭐ CE-2358 (AQ102 G) — the squad's SopConditions.UnderCommand
        //   guard: both (Entity, EntityRepository) ⇒ NoParams
        // ⭐ CE-2359 — the contact rows' SquadDefendBehavior.NotDefending guards (Entity, EntityRepository) ⇒ NoParams
        // CE-2230: the run's ONE node is the vehicle exposure cycle (lane mode) — its a1..a4 bespoke nodes are retired.
        // ⭐ CE-1410 (R-1042): every row above that named a Library/Sop, Library/Tactics or Demos/HillAttack asset went with the asset (the
        //   trees are C#-built). CE-1413 — the fixture that keeps the SOP-order and deactivator emit in the corpus: SensedFresh is the plain
        //   shared form; EqsTacticsNodes.TakeCover (ref P, ref WS, Entity, EntityRepository) the stateful one.
        ("T42_SopOrdersAndDeactivator.btree.json", "b4200000-0000-0000-0000-000000000011", BTreeDelegateShapeDto.Plain),
        ("T42_SopOrdersAndDeactivator.btree.json", "b4200000-0000-0000-0000-000000000020", BTreeDelegateShapeDto.Stateful),
    };
}
