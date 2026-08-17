using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Kernel.Data;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Hrot.Hsm.Editor.Validation;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Validation;

/// <summary>
/// ⭐⭐⭐ <b><c>DEBT-AIB-028</c>(a) — <c>StateNode.SubtreeAssetId</c> survives a trip through disk.</b>
///
/// <para>
/// 🔴🔴 <b>The measurement that filed it:</b> <i>"a NEW field, not persisted to JSON, and no real HSM
/// asset sets it"</i> ⇒ ⛔ <b>validator rules 8 and 8b could never fire on an asset loaded from
/// disk</b>, whatever a designer authored. ⭐ <c>E4</c> read as done and was not observable end to end,
/// because the field it depends on evaporated at save.
/// </para>
///
/// <para>
/// ⚠⚠ <b>Round-tripping the field is NOT the claim worth testing.</b> A mapper test would pass the
/// moment the property exists and would say nothing about the rules — the thing that was actually
/// broken. ⭐ So the second rail below authors a <b>rule-8 violation</b>, serialises, deserialises,
/// and asserts the error <b>appears on the LOADED asset</b>. 📌 That is <c>E4</c>'s missing half
/// arriving.
/// </para>
///
/// <para>
/// ⛔ <b>The PROJECTOR cannot carry it, and that is not an omission.</b> <c>HsmAssetProjector</c>
/// rebuilds a model from the compiled blob plus its <c>[HsmLayout]</c> entry, and
/// <c>SubtreeAssetId</c> is in neither — it is authoring intent, not topology. ⭐ Under JSON-SoT the
/// mapper IS the round trip that matters; inventing a layout slot for it would add a second home for
/// a field the JSON already owns.
/// </para>
/// </summary>
public sealed class HsmSubtreeAssetIdPersistenceTests
{
    private static HsmAsset MakeAsset(StateNode root, List<StateNode> all, List<RegionNode> regions)
        => new(
            Guid.NewGuid(), "SubtreeHostAsset", "", false, "",
            new HsmDefinitionBlob(),
            new MachineMetadata(),
            root, all,
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            regions,
            new List<EventDefinition>());

    /// <summary>
    /// A parallel composite with one direct child per region, both hosting <paramref name="subtreeId"/>
    /// — the exact shape rule 8 looks for.
    /// </summary>
    private static HsmAsset MakeParallelHostingAsset(Guid subtreeId)
    {
        var root     = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);

        var child0 = new StateNode("C0") { IsInitial = true, RegionIndex = 0, Parent = parallel, SubtreeAssetId = subtreeId };
        var child1 = new StateNode("C1") { RegionIndex = 1, Parent = parallel, SubtreeAssetId = subtreeId };
        parallel.Children.Add(child0);
        parallel.Children.Add(child1);

        // ⚠ InitialChild is LOAD-BEARING, not decoration: the JSON region list carries no parent
        //   reference, so HsmAssetMapper re-derives a region's owner from `InitialChild.Parent`
        //   (RHS-05). A region without one is orphaned on load — see
        //   ARegionWithNoInitialChild_IsOrphanedOnLoad_Yet.
        var rn0 = new RegionNode("R0") { RegionIndex = 0, InitialChild = child0 };
        var rn1 = new RegionNode("R1") { RegionIndex = 1, InitialChild = child1 };
        parallel.RegionNodes.Add(rn0);
        parallel.RegionNodes.Add(rn1);

        return MakeAsset(root,
            new List<StateNode> { parallel, child0, child1 },
            new List<RegionNode> { rn0, rn1 });
    }

    /// <summary>Model → DTO → JSON → DTO → model, i.e. what a save/load actually does.</summary>
    private static HsmAsset ThroughDisk(HsmAsset asset)
    {
        string json = HsmJsonServices.Serialize(HsmAssetMapper.ToDto(asset));
        return HsmAssetMapper.FromDto(HsmJsonServices.Deserialize(json)!);
    }

    // ══ rail 1 — the field survives ══════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>RED before this batch:</b> the DTO had no such property, so the value was silently
    /// dropped at <c>ToDto</c> and every loaded state came back with <c>Guid.Empty</c>.
    /// </summary>
    [Fact]
    public void ASubtreeHostingState_RoundTripsItsSubtreeAssetId_ThroughJson()
    {
        var subtreeId = new Guid("28a00000-0000-0000-0000-00000000028a");

        var restored = ThroughDisk(MakeParallelHostingAsset(subtreeId));

        var hosts = restored.AllStates.Where(s => s.SubtreeAssetId != Guid.Empty).ToList();
        Assert.Equal(2, hosts.Count);
        Assert.All(hosts, s => Assert.Equal(subtreeId, s.SubtreeAssetId));
    }

    /// <summary>⭐ A state that hosts nothing stays <c>Guid.Empty</c>, and the field is omitted from
    /// the JSON entirely — so every existing asset's bytes are untouched.</summary>
    [Fact]
    public void AStateHostingNothing_IsUnchangedAndOmittedFromJson()
    {
        var root  = new StateNode("__root__");
        var plain = new StateNode("Plain") { IsInitial = true, Parent = root };
        root.Children.Add(plain);
        var asset = MakeAsset(root, new List<StateNode> { plain }, new List<RegionNode>());

        string json = HsmJsonServices.Serialize(HsmAssetMapper.ToDto(asset));

        Assert.DoesNotContain("SubtreeAssetId", json);
        Assert.Equal(Guid.Empty, ThroughDisk(asset).AllStates.Single(s => s.Name == "Plain").SubtreeAssetId);
    }

    // ══ rail 2 — E4's missing half: the RULES fire after a load ══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The claim that matters: rule 8 fires on a DISK-LOADED asset.</b>
    ///
    /// <para>
    /// ⚠ Asserted on the asset that came BACK, not on the one that went in — the in-memory asset
    /// already errored before this batch, which is exactly why the debt was invisible: the rule
    /// worked, and nothing could ever reach it.
    /// </para>
    /// </summary>
    [Fact]
    public void ARuleEightViolation_IsReportedAfterALoad()
    {
        var subtreeId = new Guid("28b00000-0000-0000-0000-00000000028b");
        var restored  = ThroughDisk(MakeParallelHostingAsset(subtreeId));

        var diagnostics = new HsmValidator(isStatefulSubtree: id => id == subtreeId)
            .Validate(restored, blackboard: null);

        var diag = Assert.Single(diagnostics, d => d.Code == HsmDiagnosticCode.ConcurrentStatefulSubtree);
        Assert.Equal(HsmDiagnosticSeverity.Error, diag.Severity);
    }

    /// <summary>
    /// ⭐ The negative arm, so the rail above cannot pass by always-erroring: a STATELESS subtree in
    /// two regions is legal, loaded or not.
    /// </summary>
    [Fact]
    public void AStatelessSubtreeInTwoRegions_StaysLegalAfterALoad()
    {
        var restored = ThroughDisk(MakeParallelHostingAsset(new Guid("28c00000-0000-0000-0000-00000000028c")));

        var diagnostics = new HsmValidator(isStatefulSubtree: _ => false)
            .Validate(restored, blackboard: null);

        Assert.DoesNotContain(diagnostics, d => d.Code == HsmDiagnosticCode.ConcurrentStatefulSubtree);
    }

    /// <summary>
    /// ⚠⚠ <b><c>DEBT-AIB-029</c> becomes OBSERVABLE, and it is now a real defect rather than a
    /// theoretical one.</b>
    ///
    /// <para>
    /// 📐 Rule 8 walks <c>composite.Children</c> — <b>DIRECT children only</b>. ⭐ While the field was
    /// unpersisted this could not be hit from a saved asset; ⛔ <b>with the field round-tripping, a
    /// designer can now author the escape and save it.</b> A host nested one level below the region's
    /// direct child is missed, and the rule stays silent on exactly the corruption it exists to
    /// prevent.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>Asserted as the gap it is, named for it — INVERT when <c>-029</c> is fixed</b> (Batch 70's
    /// rule). ⛔ Out of scope for this item, per the handoff.
    /// </para>
    /// </summary>
    [Fact]
    public void ANestedSubtreeHost_EscapesRuleEight_Yet()
    {
        var subtreeId = new Guid("29000000-0000-0000-0000-000000000290");

        var root     = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);
        // Direct children host NOTHING; their children do — one per region.
        var c0 = new StateNode("C0") { IsInitial = true, RegionIndex = 0, Parent = parallel };
        var c1 = new StateNode("C1") { RegionIndex = 1, Parent = parallel };
        parallel.Children.Add(c0);
        parallel.Children.Add(c1);
        parallel.RegionNodes.Add(new RegionNode("R0") { RegionIndex = 0, InitialChild = c0 });
        parallel.RegionNodes.Add(new RegionNode("R1") { RegionIndex = 1, InitialChild = c1 });

        var n0 = new StateNode("N0") { IsInitial = true, RegionIndex = 0, Parent = c0, SubtreeAssetId = subtreeId };
        var n1 = new StateNode("N1") { IsInitial = true, RegionIndex = 1, Parent = c1, SubtreeAssetId = subtreeId };
        c0.Children.Add(n0);
        c1.Children.Add(n1);

        var restored = ThroughDisk(MakeAsset(root,
            new List<StateNode> { parallel, c0, c1, n0, n1 },
            new List<RegionNode> { parallel.RegionNodes[0], parallel.RegionNodes[1] }));

        // ⭐ The field DOES survive at depth — so the miss is the walk, not the persistence.
        Assert.Equal(2, restored.AllStates.Count(s => s.SubtreeAssetId == subtreeId));

        var diagnostics = new HsmValidator(isStatefulSubtree: id => id == subtreeId)
            .Validate(restored, blackboard: null);

        Assert.DoesNotContain(diagnostics, d => d.Code == HsmDiagnosticCode.ConcurrentStatefulSubtree);
    }

    /// <summary>
    /// ⚠⚠ <b>An adjacent finding, surfaced by this item: a region with NO initial child loses its
    /// OWNER on load, which silently disables rules 8 and 8b for that composite.</b>
    ///
    /// <para>
    /// 📐 <b>Measured.</b> The JSON region list carries no parent reference, so
    /// <c>HsmAssetMapper</c> re-derives ownership from <c>region.InitialChild?.Parent</c> (RHS-05).
    /// ⇒ ⛔ <c>InitialChild == null</c> means <c>owner == null</c> means the region attaches to
    /// nothing, and <c>composite.RegionNodes.Count &lt; 2</c> makes both rules skip the composite
    /// entirely — <b>no diagnostic, no warning, the asset simply validates clean.</b>
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>Same class of defect as <c>-028</c>(a) itself</b> — a rule that cannot reach its input —
    /// and it was invisible while <c>SubtreeAssetId</c> did not persist, because nothing got that far.
    /// ⛔ Out of scope here: the fix is a parent reference on <c>RegionNodeDto</c> (a persistence-shape
    /// change), not a test. ⭐ <b>Asserted as the gap it is; INVERT when ownership is persisted.</b>
    /// </para>
    /// </summary>
    [Fact]
    public void ARegionWithNoInitialChild_IsOrphanedOnLoad_Yet()
    {
        var subtreeId = new Guid("28d00000-0000-0000-0000-00000000028d");

        var root     = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);

        var child0 = new StateNode("C0") { IsInitial = true, RegionIndex = 0, Parent = parallel, SubtreeAssetId = subtreeId };
        var child1 = new StateNode("C1") { RegionIndex = 1, Parent = parallel, SubtreeAssetId = subtreeId };
        parallel.Children.Add(child0);
        parallel.Children.Add(child1);

        // ⛔ The ONLY difference from MakeParallelHostingAsset: no InitialChild.
        parallel.RegionNodes.Add(new RegionNode("R0") { RegionIndex = 0 });
        parallel.RegionNodes.Add(new RegionNode("R1") { RegionIndex = 1 });

        var restored = ThroughDisk(MakeAsset(root,
            new List<StateNode> { parallel, child0, child1 },
            new List<RegionNode> { parallel.RegionNodes[0], parallel.RegionNodes[1] }));

        // ⭐ The hosting field survived — so the miss is ownership, not persistence.
        Assert.Equal(2, restored.AllStates.Count(s => s.SubtreeAssetId == subtreeId));

        // ⛔ …and the composite came back with no regions attached, so rule 8 never looks.
        var reloadedParallel = restored.AllStates.Single(s => s.Name == "Parallel");
        Assert.Empty(reloadedParallel.RegionNodes);

        Assert.DoesNotContain(
            new HsmValidator(isStatefulSubtree: id => id == subtreeId).Validate(restored, blackboard: null),
            d => d.Code == HsmDiagnosticCode.ConcurrentStatefulSubtree);
    }
}
