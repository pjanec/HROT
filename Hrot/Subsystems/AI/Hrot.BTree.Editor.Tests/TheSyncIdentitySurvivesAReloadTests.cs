using System;
using System.Collections.Generic;
using System.Numerics;
using Fbt;
using FluentAssertions;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.BTree.Editor.Emit;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>Q49</c> OPTION C — THE SUBTREE-SYNC IDENTITY SURVIVES A RELOAD.</b>
/// 📄 <c>docs/blueprints/Architect_Question_49_Subtree_Sync_Identity_Survives_Reload.md</c>, approved by
/// the user <c>2026-08-22</c>. Closes <c>BP-342</c> <b>gap ①</b> for the editor arm.
///
/// <para>⛔⛔ <b>The defect these reproduce, and it is the reason the panel was inert.</b>
/// <c>BehaviorTreeAsset._syncNodeMeta</c>'s ONLY writer was <c>InspectorWindow:194</c> — <b>a UI
/// draw</b> — so a freshly-loaded asset had bindings but <b>no identity</b>, and
/// <c>GetApproachBSyncGroups()</c> skipped every node *(<c>:719</c> <c>continue</c>)*. ⇒ ⚠ Approach-B
/// emitted <b>nothing</b> until a designer re-opened the panel on each node.</para>
///
/// <para>⭐⭐ <b>The first rail below PINS THE DEFECT rather than assuming it</b> — 📌 the same discipline
/// as <c>TheCollisionWarningIsAnIssueRowTests</c>'s premise rail: if a future change starts populating
/// the identity elsewhere, this says so instead of silently making the fix vacuous.</para>
/// </summary>
public sealed class TheSyncIdentitySurvivesAReloadTests
{
    // ══ the defect, pinned ═══════════════════════════════════════════════════

    /// <summary>
    /// ⛔⛔ <b>A RELOADED asset — bindings, no identity — yields NO groups.</b>
    /// ⚠ This is the state after every load: <c>LoadSyncBindings</c> restores the bindings *(they are in
    /// the DTO)*, and nothing restores the identity *(it is deliberately excluded)*.
    /// </summary>
    [Fact]
    public void AReloadedAsset_HasBindingsButNoGroups_UntilTheIdentityIsRecomputed()
    {
        var (asset, _) = ReloadedAssetWithOneBoundSubtreeNode();

        asset.GetApproachBSyncGroups().Should().BeEmpty(
            "the identity is derived data and nothing has recomputed it yet");
    }

    // ══ the fix ══════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL THIS BATCH EXISTS FOR: recomputing from the catalog restores the group.</b>
    /// ⛔ Asserted on the emitted GROUP, not on the private dictionary — the group is what the emitter
    /// consumes, so this proves the consequence rather than the mechanism.
    /// </summary>
    [Fact]
    public void RecomputingFromTheCatalog_RestoresTheGroup_AndItsIdentity()
    {
        var (asset, _) = ReloadedAssetWithOneBoundSubtreeNode();

        int recomputed = asset.RecomputeSubtreeSyncIdentity(Catalog);

        recomputed.Should().Be(1);
        var group = asset.GetApproachBSyncGroups().Should().ContainSingle().Subject;
        group.SubtreeName.Should().Be("ShootBT");
        group.SubtreeDtoTypeName.Should().Be("ShootBlackboard");
        group.SubtreeDtoTypeNs.Should().Be("Hrot.Game");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>AND THE EMIT PATH CANNOT FORGET</b> — 📌 <c>R-126</c>: <i>"no path can forget to raise
    /// what is never raised."</i> ⛔ The resolver is a <b>required</b> parameter of
    /// <see cref="BTreeOrchestratorEmitter.Emit"/> and the recompute happens <b>inside</b> it, so a
    /// caller cannot emit a stale identity by omitting a step.
    /// </summary>
    [Fact]
    public void TheEmitPathRecomputesBeforeItReads()
    {
        var (asset, _) = ReloadedAssetWithOneBoundSubtreeNode();
        asset.AddVariable(new BlackboardVariableEntry("Unrelated", typeof(float), null));

        string? emitted = BTreeOrchestratorEmitter.Emit(asset, Catalog);

        // ⚠ The emitted TEXT is gap ②'s business (the master field it references does not exist yet);
        //   what this asserts is that the emitter SAW the group, which the pre-fix path could not.
        asset.GetApproachBSyncGroups().Should().ContainSingle(
            "Emit must have recomputed the identity before reading the groups");
        _ = emitted;
    }

    /// <summary>⛔ The resolver is REQUIRED — an optional one would rebuild the exact failure mode
    /// <c>Q49</c> fixes: an identity only some callers bother to supply.</summary>
    [Fact]
    public void TheEmitterRefusesToRunWithoutAResolver()
        => Assert.Throws<ArgumentNullException>(
            () => BTreeOrchestratorEmitter.Emit(MakeAsset(), null!));

    // ══ the honest edges ═════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>A MISSING subtree asset leaves an in-session identity ALONE.</b>
    /// ⚠ Deliberate: a catalog that has not finished loading must not <b>destroy</b> what a designer
    /// authored this session. ⛔ The alternative — clearing on a failed resolve — would turn a transient
    /// load order into data loss.
    /// <para>🔒 <b>Still the user's call</b> *(<c>Q49</c>'s one open sub-question)*: whether a
    /// permanently-missing subtree should also raise a <b>diagnostic row</b>. This rail pins only that it
    /// does not silently erase.</para>
    /// </summary>
    [Fact]
    public void AMissingSubtree_DoesNotEraseAnIdentityAlreadyRecorded()
    {
        var (asset, nodeId) = ReloadedAssetWithOneBoundSubtreeNode();
        asset.RecordSubtreeNodeMeta(nodeId, "ShootBT", "ShootBlackboard", "Hrot.Game");

        asset.RecomputeSubtreeSyncIdentity(_ => null).Should().Be(0);

        asset.GetApproachBSyncGroups().Should().ContainSingle();
    }

    /// <summary>⭐ Idempotent — 📌 the emit path calls it on every emit, so running twice must not
    /// double anything or drift.</summary>
    [Fact]
    public void RecomputingTwice_IsIdempotent()
    {
        var (asset, _) = ReloadedAssetWithOneBoundSubtreeNode();

        asset.RecomputeSubtreeSyncIdentity(Catalog);
        asset.RecomputeSubtreeSyncIdentity(Catalog);

        asset.GetApproachBSyncGroups().Should().ContainSingle();
    }

    /// <summary>⛔ A node with no sync bindings is not visited — the walk is over the BINDINGS, so an
    /// asset full of plain subtree calls costs nothing.</summary>
    [Fact]
    public void ASubtreeNodeWithNoBindings_IsNotRecomputed()
    {
        var asset = MakeAsset();
        AddSubtreeNode(asset, Guid.NewGuid());

        asset.RecomputeSubtreeSyncIdentity(Catalog).Should().Be(0);
    }

    // ══ the derivation is ONE implementation ═════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The panel and the reload derive the identity from the SAME function.</b>
    /// 📌 Ruling 9 — and the failure two copies would produce is silent: an emitted field name differing
    /// by one character from the one the designer was shown.
    /// </summary>
    [Fact]
    public void TheDerivationIsSharedWithTheAuthoringPanel()
    {
        var (name, dto, ns) = SubtreeSyncIdentity.Derive("Shoot BT!", "Hrot.Game.ShootBlackboard");

        name.Should().Be("ShootBT", "punctuation and spaces are stripped to a legal identifier");
        dto.Should().Be("ShootBlackboard");
        ns.Should().Be("Hrot.Game");
    }

    /// <summary>⛔ The two guards that keep the emitted field name legal — an all-punctuation asset name
    /// must not produce an EMPTY identifier, and a leading digit is not a legal identifier start.</summary>
    [Theory]
    [InlineData("!!!",    "Asset")]
    [InlineData("2Fast",  "_2Fast")]
    public void SanitizeKeepsTheIdentifierLegal(string input, string expected)
        => SubtreeSyncIdentity.SanitizeIdentifier(input).Should().Be(expected);

    /// <summary>⭐ A global-namespace type has no namespace — <see langword="null"/>, not <c>""</c>.</summary>
    [Fact]
    public void AGlobalNamespaceTypeHasNoNamespace()
        => SubtreeSyncIdentity.NsOf("ShootBlackboard").Should().BeNull();

    // ── fixture ─────────────────────────────────────────────────────────────

    private static readonly Guid SubAssetId = Guid.NewGuid();

    /// <summary>⭐ The catalog stand-in — exactly the two facts <c>PerspectiveWorkspaceRegistrar</c>'s
    /// production resolver returns from <c>catalog.FindByAssetId</c>.</summary>
    private static (string Name, string BlackboardTypeName)? Catalog(Guid id)
        => id == SubAssetId ? ("Shoot BT", "Hrot.Game.ShootBlackboard") : null;

    /// <summary>
    /// ⭐⭐ <b>An asset in the state a RELOAD leaves it in</b>: the bindings restored through
    /// <c>LoadSyncBindings</c> *(the DTO path)*, and ⛔ <b>no</b> <c>RecordSubtreeNodeMeta</c> — because
    /// nothing on the load path calls it. ⚠ That absence IS the defect.
    /// </summary>
    private static (BehaviorTreeAsset Asset, Guid NodeId) ReloadedAssetWithOneBoundSubtreeNode()
    {
        var asset  = MakeAsset();
        var nodeId = AddSubtreeNode(asset, SubAssetId);

        asset.LoadSyncBindings(new Dictionary<Guid, IReadOnlyList<SubtreeSyncBinding>>
        {
            [nodeId] = new[] { new SubtreeSyncBinding("Health", "MasterHealth", SyncIn: true, SyncOut: false) },
        });

        return (asset, nodeId);
    }

    private static Guid AddSubtreeNode(BehaviorTreeAsset asset, Guid subtreeAssetId)
    {
        var id = Guid.NewGuid();
        asset.AddNode(new BTreeEditorNode
        {
            VisualId        = id,
            KernelType      = NodeType.Subtree,
            KernelBlobIndex = 0,
            Position        = new Vector2(10, 20),
            Subtree         = new BTreeSubtreePayload { SubtreeAssetId = subtreeAssetId },
        });
        return id;
    }

    private static BehaviorTreeAsset MakeAsset(string name = "MasterAI") =>
        new BehaviorTreeAsset(
            Guid.NewGuid(), name, $"/trees/{name}.cs", true,
            "Hrot.Game.MasterBlackboard", "Hrot.Game.MasterContext",
            new BehaviorTreeBlob
            {
                TreeName = "T", Nodes = Array.Empty<NodeDefinition>(),
                MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
                IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
            },
            "Hrot.AI.Behaviors.Trees");
}
