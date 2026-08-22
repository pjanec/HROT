using System;
using System.Collections.Generic;
using FluentAssertions;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.Emit;

/// <summary>
/// ⭐⭐⭐ <b><c>Q50</c> OPTION A — THE MASTER BLACKBOARD DECLARES THE SUB-TREE SLICE.</b>
/// 🔒 <b>User, <c>2026-08-22</c>:</b> <i>"i hoped the editor automatically adds the subtree's data, which
/// is likely the option A."</i>
///
/// <para>⛔⛔ <b>The defect these close</b> *(<c>BP-342</c> gap ②)*: <c>BTreeOrchestratorEmitCore:165</c>
/// emits <c>ref var subDto = ref master.{SubtreeName}_{DtoTypeName}</c> — and <b>no blackboard emitter
/// declared that field</b>. ⇒ ⚠ the orchestrator referenced a member of a struct that did not have it,
/// so Approach-B could never ship: emitting it would have broken the build the moment a designer created
/// a sync binding *(📌 <c>BP-306</c>'s shape)</para>
///
/// <para>⭐⭐ <b>The load-bearing pair is the last two rails:</b> the field the projection DECLARES and
/// the field the emit core WRITES must be the same string. ⛔ A one-character divergence is a build break
/// with no obvious cause — which is why both call <see cref="SubtreeSyncProjection.SliceFieldName"/> and
/// why a rail asserts they agree rather than trusting that they do.</para>
/// </summary>
public sealed class TheMasterDeclaresTheSubtreeSliceTests
{
    private static readonly Guid SubAssetId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string SubBbType = "Hrot.Game.ShootBlackboard";

    // ══ the projection ═══════════════════════════════════════════════════════

    /// <summary>⭐⭐⭐ <b>One bound subtree node ⇒ one group AND one slice field.</b> ⛔ They come from
    /// ONE walk, so a node can never contribute a group without the field it writes through.</summary>
    [Fact]
    public void ABoundSubtreeNode_YieldsAGroupAndItsSliceField()
    {
        var (groups, slices) = SubtreeSyncProjection.Project(DtoWithOneBoundSubtree(), Catalog);

        var group = groups.Should().ContainSingle().Subject;
        group.SubtreeName.Should().Be("ShootBT");
        group.SubtreeDtoTypeName.Should().Be("ShootBlackboard");
        group.SubtreeDtoTypeNs.Should().Be("Hrot.Game");

        var slice = slices.Should().ContainSingle().Subject;
        slice.FieldName.Should().Be("ShootBT_ShootBlackboard");
        slice.TypeId.Should().Be(SubBbType, "the slice IS the callee's blackboard, so its type is that type");
    }

    /// <summary>
    /// ⛔⛔ <b>AN UNRESOLVABLE CALLEE YIELDS NEITHER — and that is the whole safety property.</b>
    /// ⚠ A group without its field is the broken-build state; a field without its group is dead weight in
    /// the blackboard. ⇒ ⭐ the projection emits both or neither, never one.
    /// </summary>
    [Fact]
    public void AnUnresolvableCallee_YieldsNeitherAGroupNorAField()
    {
        var (groups, slices) = SubtreeSyncProjection.Project(DtoWithOneBoundSubtree(), _ => null);

        groups.Should().BeEmpty();
        slices.Should().BeEmpty();
    }

    /// <summary>⛔ No bindings ⇒ nothing at all. ⚠ This is every asset in today's corpus, which is why
    /// the change is byte-identical on it.</summary>
    [Fact]
    public void NoBindings_YieldNothing()
    {
        var dto = DtoWithOneBoundSubtree();
        dto.SubtreeSyncBindings.Clear();

        var (groups, slices) = SubtreeSyncProjection.Project(dto, Catalog);

        groups.Should().BeEmpty();
        slices.Should().BeEmpty();
    }

    /// <summary>⛔ A binding whose node is not a Subtree node is skipped — the bindings dictionary is
    /// keyed by node id, and a stale key must not manufacture a group.</summary>
    [Fact]
    public void ABindingOnAMissingNode_IsSkipped()
    {
        var dto = DtoWithOneBoundSubtree();
        dto.Nodes.Clear();

        SubtreeSyncProjection.Project(dto, Catalog).Groups.Should().BeEmpty();
    }

    /// <summary>⭐ The identity is derived by the SAME function the authoring panel and the reload
    /// recompute use — 📌 ruling 9, asserted rather than assumed.</summary>
    [Fact]
    public void TheIdentityMatchesTheSharedDerivation()
    {
        var dto = DtoWithOneBoundSubtree();
        dto.Nodes[0] = SubtreeNode(((BTreeSubtreeNodeDto)dto.Nodes[0]).VisualId, "Shoot BT!");

        var expected = SubtreeSyncIdentity.Derive("Shoot BT!", SubBbType);
        var group    = SubtreeSyncProjection.Project(dto, Catalog).Groups[0];

        group.SubtreeName.Should().Be(expected.SubtreeName);
        group.SubtreeDtoTypeName.Should().Be(expected.SubDtoTypeName);
    }

    // ══ declared name == written name ════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL GAP ② EXISTS FOR: what is DECLARED is exactly what is WRITTEN.</b>
    /// ⛔ Asserted against the emitted TEXT, so a change to either side that breaks the agreement
    /// reddens here rather than in a downstream build with an unexplained CS1061.
    /// </summary>
    [Fact]
    public void TheDeclaredFieldIsTheFieldTheOrchestratorWrites()
    {
        var dto = DtoWithOneBoundSubtree();
        var (groups, slices) = SubtreeSyncProjection.Project(dto, Catalog);

        string emitted = BTreeOrchestratorEmitCore.Emit(dto, groups)!;

        emitted.Should().NotBeNull();
        emitted.Should().Contain("ref master." + slices[0].FieldName,
            "the orchestrator must write through the field the projection declares");
    }

    /// <summary>⭐ And the composer is shared, so neither side spells the name out.</summary>
    [Fact]
    public void TheSliceFieldNameIsComposedInOnePlace()
        => SubtreeSyncProjection.SliceFieldName("ShootBT", "ShootBlackboard")
            .Should().Be("ShootBT_ShootBlackboard");

    // ── fixture ─────────────────────────────────────────────────────────────

    private static string? Catalog(Guid id) => id == SubAssetId ? SubBbType : null;

    private static BTreeSubtreeNodeDto SubtreeNode(Guid visualId, string subtreeName) =>
        new BTreeSubtreeNodeDto
        {
            VisualId = visualId,
            Subtree  = new BTreeSubtreePayloadDto
            {
                SubtreeAssetId = SubAssetId,
                SubtreeName    = subtreeName,
                IsResolved     = true,
            },
        };

    /// <summary>⭐ A master tree with one subtree node that has one sync binding — ⛔ built entirely from
    /// PERSISTED shapes, which is the point: the projection reads a document, not a live object graph,
    /// so there is no "after the catalog loads" ordering to get wrong.</summary>
    private static BehaviorTreeAssetDto DtoWithOneBoundSubtree()
    {
        var nodeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var dto = new BehaviorTreeAssetDto
        {
            AssetId            = Guid.NewGuid(),
            Name               = "MasterAI",
            TargetNamespace    = "Hrot.AI.Behaviors.Trees",
            BlackboardTypeName = "Hrot.Game.MasterBlackboard",
            ContextTypeName    = "Hrot.Game.MasterContext",
        };
        dto.Nodes.Add(SubtreeNode(nodeId, "Shoot BT"));
        dto.SubtreeSyncBindings[nodeId.ToString()] = new List<SubtreeSyncBindingDto>
        {
            new SubtreeSyncBindingDto
            {
                FieldName = "Health", MasterVariableName = "MasterHealth",
                SyncIn = true, SyncOut = false,
            },
        };
        return dto;
    }
}
