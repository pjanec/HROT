using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BTreeSubtreeResolverTests
{
    // ---- Fake catalog -------------------------------------------------------

    private sealed class FakeCatalog : IAssetCatalog
    {
        private readonly List<IEditableAsset> _assets = new();
        public IReadOnlyList<IEditableAsset> All => _assets;
#pragma warning disable CS0067
        public event Action<AssetKind>? Changed;
#pragma warning restore CS0067
        public IEditableAsset? FindByAssetId(Guid id) =>
            _assets.FirstOrDefault(a => a.AssetId == id);
        public IEditableAsset? FindByName(string name) =>
            _assets.FirstOrDefault(a => a.Name == name);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) =>
            System.Array.Empty<IEditableAsset>();
        public void Add(IEditableAsset asset) => _assets.Add(asset);
    }

    // ---- Helpers ------------------------------------------------------------

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "T",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset(string name = "Host") =>
        new BehaviorTreeAsset(Guid.NewGuid(), name, $"/{name}.cs", true, "BB", "Ctx", EmptyBlob());

    private static BTreeEditorNode MakeSubtreeNode(string subtreeName)
    {
        return new BTreeEditorNode
        {
            VisualId    = Guid.NewGuid(),
            KernelType  = NodeType.Subtree,
            KernelBlobIndex = -1,
            Subtree     = new BTreeSubtreePayload { SubtreeName = subtreeName },
        };
    }

    private static BTreeEditorNode MakeActionNode()
    {
        return new BTreeEditorNode
        {
            VisualId    = Guid.NewGuid(),
            KernelType  = NodeType.Action,
            KernelBlobIndex = -1,
            Action      = new BehaviorActionBinding { MethodFqn = "Ns.C.M" },
        };
    }

    // ---- Tests --------------------------------------------------------------

    [Fact]
    public void Resolve_known_subtree_name_sets_is_resolved_true()
    {
        var catalog = new FakeCatalog();
        var subAsset = MakeAsset("SubTree1");
        catalog.Add(subAsset);

        var hostAsset = MakeAsset("Host");
        var subtreeNode = MakeSubtreeNode("SubTree1");
        hostAsset.AddNode(subtreeNode);

        BTreeSubtreeResolver.Resolve(hostAsset, catalog);

        subtreeNode.Subtree!.IsResolved.Should().BeTrue();
        subtreeNode.Subtree.SubtreeAssetId.Should().Be(subAsset.AssetId);
    }

    [Fact]
    public void Resolve_unknown_subtree_name_sets_is_resolved_false()
    {
        var catalog   = new FakeCatalog();
        var hostAsset = MakeAsset("Host");
        var subtreeNode = MakeSubtreeNode("DoesNotExist");
        hostAsset.AddNode(subtreeNode);

        BTreeSubtreeResolver.Resolve(hostAsset, catalog);

        subtreeNode.Subtree!.IsResolved.Should().BeFalse();
        subtreeNode.Subtree.SubtreeAssetId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Resolve_non_subtree_nodes_are_unchanged()
    {
        var catalog   = new FakeCatalog();
        var hostAsset = MakeAsset("Host");
        var actionNode = MakeActionNode();
        hostAsset.AddNode(actionNode);

        BTreeSubtreeResolver.Resolve(hostAsset, catalog);

        // Action node must be unchanged; no subtree payload.
        actionNode.Subtree.Should().BeNull();
        actionNode.Action.Should().NotBeNull();
        actionNode.Action!.MethodFqn.Should().Be("Ns.C.M");
    }

    // ── §S1 ② — THE HEAL RULE, and the defect it fixes ───────────────────────
    // 📄 BTree_Editor_NodeEditor_Host_Design.md §S1; the rule itself is
    //    AI_Editor_Shared_Infrastructure.md §7.1a.

    /// <summary>
    /// ⭐⭐⭐ <b>A renamed asset heals its own reference.</b> The name on disk is stale but the Guid
    /// still resolves ⇒ the name is rewritten and the caller is told, so it can mark the asset dirty.
    /// </summary>
    [Fact]
    public void Resolve_heals_the_name_from_the_guid_after_a_rename()
    {
        var catalog  = new FakeCatalog();
        var subAsset = MakeAsset("SubTree_v2");     // ⭐ renamed since the host was saved
        catalog.Add(subAsset);

        var hostAsset   = MakeAsset("Host");
        var subtreeNode = MakeSubtreeNode("SubTree_v1");
        subtreeNode.Subtree!.SubtreeAssetId = subAsset.AssetId;   // the rename survivor
        hostAsset.AddNode(subtreeNode);

        int healed = BTreeSubtreeResolver.Resolve(hostAsset, catalog);

        healed.Should().Be(1);
        subtreeNode.Subtree.SubtreeName.Should().Be("SubTree_v2");
        subtreeNode.Subtree.IsResolved.Should().BeTrue();
    }

    /// <summary>
    /// ⛔⛔⛔ <b>THE DEFECT RAIL — a dangling reference KEEPS ITS GUID.</b>
    ///
    /// <para>🔴 This resolver used to run <c>payload.SubtreeAssetId = Guid.Empty</c> whenever the
    /// name missed. ⚠ <b>A missed name IS the rename case</b>, so it destroyed the only field that
    /// could still identify the asset — and <c>BTreeSubtreePayloadDto</c> persists that Guid, so the
    /// information to heal was on disk and was being thrown away.</para>
    ///
    /// <para>🔒 The defect survived because nothing asserted what a FAILED resolution must
    /// <b>preserve</b>: the existing "unknown name ⇒ IsResolved false" rail passed either way.</para>
    /// </summary>
    [Fact]
    public void Resolve_never_erases_the_guid_of_a_dangling_reference()
    {
        var catalog    = new FakeCatalog();          // ⛔ empty: neither name nor id resolves
        var strangerId = Guid.NewGuid();

        var hostAsset   = MakeAsset("Host");
        var subtreeNode = MakeSubtreeNode("Vanished");
        subtreeNode.Subtree!.SubtreeAssetId = strangerId;
        hostAsset.AddNode(subtreeNode);

        BTreeSubtreeResolver.Resolve(hostAsset, catalog);

        subtreeNode.Subtree.IsResolved.Should().BeFalse();
        subtreeNode.Subtree.SubtreeName.Should().Be("Vanished");        // ⛔ kept
        subtreeNode.Subtree.SubtreeAssetId.Should().Be(strangerId);     // ⛔ kept — was Guid.Empty
    }

    /// <summary>⭐ The name wins when BOTH resolve — it is what the designer last picked.</summary>
    [Fact]
    public void Resolve_prefers_the_name_when_both_resolve()
    {
        var catalog = new FakeCatalog();
        var byName  = MakeAsset("Wanted");
        var byId    = MakeAsset("Other");
        catalog.Add(byName);
        catalog.Add(byId);

        var hostAsset   = MakeAsset("Host");
        var subtreeNode = MakeSubtreeNode("Wanted");
        subtreeNode.Subtree!.SubtreeAssetId = byId.AssetId;   // a stale, still-valid id
        hostAsset.AddNode(subtreeNode);

        int healed = BTreeSubtreeResolver.Resolve(hostAsset, catalog);

        healed.Should().Be(0);
        subtreeNode.Subtree.SubtreeName.Should().Be("Wanted");
        subtreeNode.Subtree.SubtreeAssetId.Should().Be(byName.AssetId);  // ⭐ re-pointed to the NAME's asset
    }
}
