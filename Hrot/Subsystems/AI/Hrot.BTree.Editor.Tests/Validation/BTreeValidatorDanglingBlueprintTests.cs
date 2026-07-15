using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Validation;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.References;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Validation;

/// <summary>
/// Phase C (AIE-053): authoring-time dangling-reference validation for composed AiPrimitive
/// nodes. <see cref="BTreeValidator"/>'s historical single-arg <c>Validate(asset)</c> overload
/// is deliberately left unchanged — the dangling-blueprint rule needs an <see cref="IAssetCatalog"/>
/// (external context), so it only runs when the caller opts in via the new optional parameter.
/// <para>
/// Uses plain-string composed FQNs + a fake blueprint that reports a chosen
/// <see cref="IComposedBlueprintIdentity.GeneratedClassName"/> — no hash/sanitize here.
/// </para>
/// </summary>
public sealed class BTreeValidatorDanglingBlueprintTests
{
    private const string ClassName = "ParamDemo_CEFE162F_Bp";
    private static string ComposedFqn(string className = ClassName, string method = "TickCore") =>
        $"{ComposedBlueprintResolver.GeneratedNamespace}.{className}.{method}";

    // ---- helpers ------------------------------------------------------------

    private sealed class FakeBlueprint : IEditableAsset, IComposedBlueprintIdentity
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "Fake";
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string SourceFilePath { get; init; } = string.Empty;
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
        public string? GeneratedClassName { get; init; }
        public event Action? Changed { add { } remove { } }
    }

    private sealed class FakeCatalog : IAssetCatalog
    {
        private readonly List<IEditableAsset> _assets;
        public FakeCatalog(params IEditableAsset[] assets) => _assets = new List<IEditableAsset>(assets);
        public IReadOnlyList<IEditableAsset> All => _assets;
        public IEditableAsset? FindByAssetId(Guid assetId) => _assets.FirstOrDefault(a => a.AssetId == assetId);
        public IEditableAsset? FindByName(string name) => _assets.FirstOrDefault(a => a.Name == name);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) => Array.Empty<IEditableAsset>();
        public event Action<AssetKind>? Changed { add { } remove { } }
    }

    private static BehaviorTreeBlob EmptyBlob() => new BehaviorTreeBlob
    {
        TreeName        = "T",
        Nodes           = Array.Empty<NodeDefinition>(),
        MethodNames     = Array.Empty<string>(),
        FloatParams     = Array.Empty<float>(),
        IntParams       = Array.Empty<int>(),
        SubtreeAssetIds = Array.Empty<string>(),
    };

    private static BehaviorTreeAsset MakeAssetWithComposedAction(string methodFqn, out Guid nodeId)
    {
        var asset = new BehaviorTreeAsset(Guid.NewGuid(), "T", "/T.cs", true, "BB", "Ctx", EmptyBlob());
        nodeId = Guid.NewGuid();

        var root   = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Root };
        var action = new BTreeEditorNode
        {
            VisualId     = nodeId,
            KernelType   = NodeType.Action,
            DisplayLabel = "ComposedAction",
            Action       = new BTreeActionPayload
            {
                MethodFqn     = methodFqn,
                DelegateShape = BTreeActionDelegateShape.AiPrimitiveTickCore,
            },
        };
        root.ChildVisualIds.Add(action.VisualId);

        asset.ReplaceAll(new List<BTreeEditorNode> { root, action }, new List<BTreeEditorPill>(), EmptyBlob());
        return asset;
    }

    // ---- Tests ----------------------------------------------------------------

    [Fact]
    public void Validate_composed_action_with_resolvable_blueprint_is_clean()
    {
        var blueprint = new FakeBlueprint { Name = "Param Demo", GeneratedClassName = ClassName };
        var catalog   = new FakeCatalog(blueprint);

        var asset = MakeAssetWithComposedAction(ComposedFqn(), out _);

        var diagnostics = new BTreeValidator().Validate(asset, catalog);

        diagnostics.Should().NotContain(d => d.Code == BTreeDiagnosticCode.DanglingReferenceAfterReload);
    }

    [Fact]
    public void Validate_composed_action_with_deleted_blueprint_reports_dangling_reference()
    {
        var catalog = new FakeCatalog(); // empty catalog — nothing resolves
        var asset = MakeAssetWithComposedAction(ComposedFqn(), out var nodeId);

        var diagnostics = new BTreeValidator().Validate(asset, catalog);

        diagnostics.Should().ContainSingle(d =>
            d.Code == BTreeDiagnosticCode.DanglingReferenceAfterReload &&
            d.Severity == BTreeDiagnosticSeverity.Error &&
            d.VisualId == nodeId);

        var diag = diagnostics.Single(d => d.Code == BTreeDiagnosticCode.DanglingReferenceAfterReload);
        diag.Message.Should().Contain("no longer exists");
    }

    [Fact]
    public void Validate_composed_action_with_renamed_blueprint_reports_dangling_reference()
    {
        // The blueprint still exists but now reports a different generated class name (renamed),
        // so the composed node's FQN no longer matches.
        var renamed = new FakeBlueprint { GeneratedClassName = "SomethingElse_CEFE162F_Bp" };
        var catalog = new FakeCatalog(renamed);

        var asset = MakeAssetWithComposedAction(ComposedFqn(), out _);

        var diagnostics = new BTreeValidator().Validate(asset, catalog);

        diagnostics.Should().Contain(d => d.Code == BTreeDiagnosticCode.DanglingReferenceAfterReload);
    }

    [Fact]
    public void Validate_without_catalog_skips_the_dangling_blueprint_rule()
    {
        // Historical single-arg call site (e.g. BTreeGraphModel.BuildCaches) must keep behaving
        // exactly as before — the rule is external-context-only and opt-in.
        var asset = MakeAssetWithComposedAction(ComposedFqn(), out _);

        var diagnostics = new BTreeValidator().Validate(asset); // no catalog argument

        diagnostics.Should().NotContain(d => d.Code == BTreeDiagnosticCode.DanglingReferenceAfterReload);
    }

    [Fact]
    public void Validate_ordinary_hand_written_action_is_never_flagged_as_dangling()
    {
        var catalog = new FakeCatalog(); // empty — but rule shouldn't even look, since not composed
        var asset = MakeAssetWithNonComposedAction("Hrot.Game.Combat.CombatActions.AimAndFire");

        var diagnostics = new BTreeValidator().Validate(asset, catalog);

        diagnostics.Should().NotContain(d => d.Code == BTreeDiagnosticCode.DanglingReferenceAfterReload);
    }

    private static BehaviorTreeAsset MakeAssetWithNonComposedAction(string methodFqn)
    {
        var asset = new BehaviorTreeAsset(Guid.NewGuid(), "T", "/T.cs", true, "BB", "Ctx", EmptyBlob());
        var root   = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Root };
        var action = new BTreeEditorNode
        {
            VisualId     = Guid.NewGuid(),
            KernelType   = NodeType.Action,
            DisplayLabel = "HandWrittenAction",
            Action       = new BTreeActionPayload
            {
                MethodFqn     = methodFqn,
                DelegateShape = BTreeActionDelegateShape.ThreeParamReusable,
            },
        };
        root.ChildVisualIds.Add(action.VisualId);
        asset.ReplaceAll(new List<BTreeEditorNode> { root, action }, new List<BTreeEditorPill>(), EmptyBlob());
        return asset;
    }
}
