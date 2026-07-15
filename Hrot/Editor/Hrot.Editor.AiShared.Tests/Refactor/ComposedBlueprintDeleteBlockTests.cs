using System;
using System.Collections.Generic;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Model;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Editor.Catalog;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Refactor;

/// <summary>
/// Phase C (AIE-053), deliverable 4: proves <see cref="RefactorService.ApplyDelete"/> refuses to
/// delete a Blueprint asset that a BTree composes as an AiPrimitive node (the composed reference
/// is classified <see cref="ReferenceCriticality.Critical"/> — see
/// <see cref="RefactorService"/>'s ClassifyReference), and allows the delete once no such
/// reference exists. Uses the REAL production wiring: <see cref="BlueprintReferenceContributor"/>
/// (Blueprint side) + <see cref="BTreeComposedBlueprintReferenceContributor"/> (BTree side) +
/// <see cref="ReferenceCatalog"/> + <see cref="RefactorService"/> — not a hand-rolled fake catalog
/// — so this is an end-to-end check of the actual cross-asset wiring.
/// <para>
/// This test file is intentionally the compiler-coupled part of the shared-editor test project:
/// it constructs the real (internal) <see cref="BlueprintFileAsset"/> (whose
/// <see cref="ComposedBlueprintResolver"/>-matching generated class name is computed on the
/// blueprint-editor side via <see cref="BlueprintIdHash"/>+<see cref="Sanitizer"/>) and builds
/// composed node FQNs from its precomputed <c>GeneratedClassName</c>. It also carries the
/// hash-correctness cross-check that keeps <c>BlueprintFileAsset.GeneratedClassName</c> in agreement
/// with the compiler — the shared <see cref="ComposedBlueprintResolver"/> itself has no such
/// dependency and is tested separately with plain strings.
/// </para>
/// </summary>
public sealed class ComposedBlueprintDeleteBlockTests
{
    // ---- helpers --------------------------------------------------------------

    private sealed class ListAssetCatalogContributor : IAssetCatalogContributor
    {
        private readonly List<IEditableAsset> _assets;
        public ListAssetCatalogContributor(AssetKind kind, params IEditableAsset[] assets)
        {
            Kind = kind;
            _assets = new List<IEditableAsset>(assets);
        }
        public AssetKind Kind { get; }
        public IReadOnlyList<IEditableAsset> Enumerate() => _assets;
        public event Action? ContributorChanged;
        public void Fire() => ContributorChanged?.Invoke();
    }

    private static BehaviorTreeBlob EmptyBlob() => new BehaviorTreeBlob
    {
        TreeName        = "test",
        Nodes           = Array.Empty<NodeDefinition>(),
        MethodNames     = Array.Empty<string>(),
        FloatParams     = Array.Empty<float>(),
        IntParams       = Array.Empty<int>(),
        SubtreeAssetIds = Array.Empty<string>(),
    };

    // Builds a composed node FQN from the blueprint's precomputed generated class name — exactly
    // what the editor stores on a placed AiPrimitive node.
    private static string ComposedFqnFor(BlueprintFileAsset bp) =>
        $"{ComposedBlueprintResolver.GeneratedNamespace}.{bp.GeneratedClassName}.{ComposedBlueprintResolver.DefaultMethodName}";

    private static BehaviorTreeAsset MakeBTreeComposing(string methodFqn)
    {
        var asset = new BehaviorTreeAsset(Guid.NewGuid(), "ComposingTree", "/tree.cs", true, "BB", "Ctx", EmptyBlob());
        var node = new BTreeEditorNode
        {
            VisualId     = Guid.NewGuid(),
            KernelType   = NodeType.Action,
            DisplayLabel = "ComposedAction",
            Action       = new BTreeActionPayload
            {
                MethodFqn     = methodFqn,
                DelegateShape = BTreeActionDelegateShape.AiPrimitiveTickCore,
            },
        };
        asset.ReplaceAll(new List<BTreeEditorNode> { node }, new List<BTreeEditorPill>(), EmptyBlob());
        return asset;
    }

    /// <summary>
    /// Builds the full production stack: AssetCatalog with the given assets, ReferenceCatalog
    /// with the real Blueprint + BTree composed-reference contributors, and a RefactorService
    /// over both.
    /// </summary>
    private static (AssetCatalog assetCatalog, RefactorService refactor) BuildStack(params IEditableAsset[] assets)
    {
        var byKind = new Dictionary<AssetKind, List<IEditableAsset>>();
        foreach (var a in assets)
        {
            if (!byKind.TryGetValue(a.Kind, out var list))
                byKind[a.Kind] = list = new List<IEditableAsset>();
            list.Add(a);
        }

        var assetCatalog = new AssetCatalog();
        ListAssetCatalogContributor? lastContributor = null;
        foreach (var (kind, list) in byKind)
        {
            var contributor = new ListAssetCatalogContributor(kind, list.ToArray());
            assetCatalog.AddContributor(contributor); // synchronously rebuilds _cache
            lastContributor = contributor;
        }

        var referenceCatalog = new ReferenceCatalog(assetCatalog, new IReferenceCatalogContributor[]
        {
            new BlueprintReferenceContributor(),
            new BTreeComposedBlueprintReferenceContributor(),
        });

        // ReferenceCatalog only (re)populates on catalog.Changed; AddContributor's own Rebuild()
        // doesn't fire Changed, so fire it once now that every contributor's assets are in place.
        lastContributor?.Fire();

        var refactor = new RefactorService(referenceCatalog, assetCatalog, new AtomicMultiFileWriter());
        return (assetCatalog, refactor);
    }

    // ---- Hash-correctness cross-check (compiler-coupled) ----------------------

    [Fact]
    public void BlueprintFileAsset_GeneratedClassName_matches_compiler_hash_and_sanitizer()
    {
        var id   = Guid.NewGuid();
        var name = "Param Demo";
        var bp   = new BlueprintFileAsset(id, name, "/blueprints/paramdemo.bp.json");

        var expected = $"{Sanitizer.SanitizeName(name)}_{BlueprintIdHash.Compute(id):X8}_Bp";

        bp.GeneratedClassName.Should().Be(expected);
        ((IComposedBlueprintIdentity)bp).GeneratedClassName.Should().Be(expected);
    }

    // ---- Delete-block tests ---------------------------------------------------

    [Fact]
    public void ApplyDelete_refuses_when_a_BTree_composes_the_blueprint()
    {
        var blueprint = new BlueprintFileAsset(Guid.NewGuid(), "Param Demo", "/blueprints/paramdemo.bp.json");
        var btree = MakeBTreeComposing(ComposedFqnFor(blueprint));

        var (_, refactor) = BuildStack(blueprint, btree);

        var preview = refactor.PreviewDelete(blueprint.AssetId, new DeleteOptions(AllowDanglingReferences: false));
        preview.CriticalReferences.Should().NotBeEmpty("the BTree's composed node is a Critical ActionFqn reference");

        var result = refactor.ApplyDelete(preview);

        result.Success.Should().BeFalse("deleting a blueprint a BTree composes must be refused");
        result.FailureReason.Should().Contain("critical");
    }

    [Fact]
    public void ApplyDelete_allows_deletion_when_no_BTree_composes_the_blueprint()
    {
        var blueprint = new BlueprintFileAsset(Guid.NewGuid(), "Unused Blueprint", "/blueprints/unused.bp.json");

        var (_, refactor) = BuildStack(blueprint); // no composing BTree at all

        var preview = refactor.PreviewDelete(blueprint.AssetId, new DeleteOptions(AllowDanglingReferences: false));
        preview.CriticalReferences.Should().BeEmpty();

        var result = refactor.ApplyDelete(preview);

        result.Success.Should().BeTrue("no BTree references this blueprint, so deletion should proceed");
    }

    [Fact]
    public void PreviewDelete_classifies_the_composed_reference_as_Critical()
    {
        var blueprint = new BlueprintFileAsset(Guid.NewGuid(), "Param Demo", "/blueprints/paramdemo.bp.json");
        var btree = MakeBTreeComposing(ComposedFqnFor(blueprint));

        var (_, refactor) = BuildStack(blueprint, btree);

        var preview = refactor.PreviewDelete(blueprint.AssetId, new DeleteOptions());

        preview.ClassifiedReferences.Should().Contain(c =>
            c.Criticality == ReferenceCriticality.Critical &&
            (c.Reference.TargetKind == SubElementKind.ActionFqn || c.Reference.TargetKind == SubElementKind.ConditionFqn) &&
            c.Reference.HostAssetId == btree.AssetId);
    }
}
