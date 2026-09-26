using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Validation;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Validation;

/// <summary>
/// ⭐⭐⭐ <b><c>E5</c> item 7 — <c>SubtreeAssetCycle</c> on the BTree side.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.16.
///
/// <para>⭐⭐ <b>This is the arm that can fire on a SHIPPED asset.</b> The HSM side has no authoring
/// gesture that writes <c>StateNode.SubtreeAssetId</c> yet, but <c>BTreeSubtreePayload</c> is
/// authored and persisted — so a designer can build this ring today.</para>
///
/// <para>⛔⛔ <b>Do not confuse this with <c>BTreeDiagnosticCode.CycleDetected</c>.</b> That rule walks
/// <c>ChildVisualIds</c> — the NODE graph inside one asset. 📐 A tree can be perfectly acyclic by that
/// rule while hosting a sub-tree that hosts it back, which is exactly what
/// <see cref="TwoAssets_HostingEachOther_AreAHardError"/> builds.</para>
/// </summary>
public sealed class BTreeSubtreeAssetCycleTests
{
    private static BehaviorTreeAsset MakeAsset(string name) => new(
        Guid.NewGuid(), name, $"/{name}.btree.json", true, "BB", "Ctx",
        new BehaviorTreeBlob
        {
            TreeName        = name,
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        });

    /// <summary>Attach a Subtree node to <paramref name="host"/> pointing at <paramref name="childId"/>.</summary>
    private static void HostSubtree(BehaviorTreeAsset host, Guid childId)
    {
        var node = new BTreeEditorNode
        {
            VisualId   = Guid.NewGuid(),
            KernelType = NodeType.Subtree,
            Subtree    = new BTreeSubtreePayload { SubtreeAssetId = childId, IsResolved = true },
        };
        host.AddNode(node);
    }

    private sealed class FakeCatalog : IAssetCatalog
    {
        private readonly Dictionary<Guid, IEditableAsset> _byId = new();
        public FakeCatalog(params IEditableAsset[] assets)
        {
            foreach (var a in assets) _byId[a.AssetId] = a;
        }
        public IReadOnlyList<IEditableAsset> All => _byId.Values.ToList();
        public IEditableAsset? FindByAssetId(Guid id) => _byId.GetValueOrDefault(id);
        public IEditableAsset? FindByName(string n) => _byId.Values.FirstOrDefault(a => a.Name == n);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid id) => Array.Empty<IEditableAsset>();
#pragma warning disable 67
        public event Action<AssetKind>? Changed;
#pragma warning restore 67
    }

    [Fact]
    public void TwoAssets_HostingEachOther_AreAHardError()
    {
        var a = MakeAsset("TreeA");
        var b = MakeAsset("TreeB");
        HostSubtree(a, b.AssetId);
        HostSubtree(b, a.AssetId);

        var d = new BTreeValidator().Validate(a, new FakeCatalog(a, b));

        var cycle = d.Should().ContainSingle(x => x.Code == BTreeDiagnosticCode.SubtreeAssetCycle).Subject;
        cycle.Severity.Should().Be(BTreeDiagnosticSeverity.Error);
        cycle.Message.Should().Contain("TreeA").And.Contain("TreeB");
    }

    /// <summary>🔴 The red-proof control: the same two assets, one edge removed.</summary>
    [Fact]
    public void OrdinaryHosting_IsNotACycle()
    {
        var a = MakeAsset("TreeA");
        var b = MakeAsset("TreeB");
        HostSubtree(a, b.AssetId);

        var d = new BTreeValidator().Validate(a, new FakeCatalog(a, b));

        d.Should().NotContain(x => x.Code == BTreeDiagnosticCode.SubtreeAssetCycle);
    }

    /// <summary>
    /// ⚠ <b>Without a catalogue the rule is skipped</b>, exactly as
    /// <c>CheckDanglingBlueprintReferences</c> is — pinned so the silence is a documented skip.
    /// </summary>
    [Fact]
    public void WithNoCatalog_TheRuleIsSkipped()
    {
        var a = MakeAsset("TreeA");
        var b = MakeAsset("TreeB");
        HostSubtree(a, b.AssetId);
        HostSubtree(b, a.AssetId);

        new BTreeValidator().Validate(a)
            .Should().NotContain(x => x.Code == BTreeDiagnosticCode.SubtreeAssetCycle);
    }

    /// <summary>
    /// ⛔ An UNRESOLVED subtree node carries <c>Guid.Empty</c> and contributes no edge —
    /// <c>UnresolvedSubtree</c> is that defect's rule, not this one.
    /// </summary>
    [Fact]
    public void AnUnresolvedSubtreeNode_ContributesNoEdge()
    {
        var a = MakeAsset("TreeA");
        HostSubtree(a, Guid.Empty);

        a.GetHostedSubtreeAssetIds().Should().BeEmpty();
        new BTreeValidator().Validate(a, new FakeCatalog(a))
            .Should().NotContain(x => x.Code == BTreeDiagnosticCode.SubtreeAssetCycle);
    }

    /// <summary>⭐ The forward edge itself: deduplicated, no <see cref="Guid.Empty"/>.</summary>
    [Fact]
    public void GetHostedSubtreeAssetIds_IsADedupedSet()
    {
        var a = MakeAsset("TreeA");
        var childId = Guid.NewGuid();
        HostSubtree(a, childId);
        HostSubtree(a, childId);

        a.GetHostedSubtreeAssetIds().Should().BeEquivalentTo(new[] { childId });
    }
}
