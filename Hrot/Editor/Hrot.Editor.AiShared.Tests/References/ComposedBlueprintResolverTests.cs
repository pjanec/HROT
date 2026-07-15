using FluentAssertions;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.References;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.References;

/// <summary>
/// Phase C (AIE-053): tests for <see cref="ComposedBlueprintResolver"/> — the FQN→AssetId resolver
/// a composed BTree node uses to find the Blueprint asset it was placed onto.
/// <para>
/// Identity is by FQN string, never by a persisted AssetId. Crucially, the resolver contains NO
/// hash/sanitize logic — it matches the node FQN's declaring-type name against the blueprint's
/// precomputed <see cref="IComposedBlueprintIdentity.GeneratedClassName"/>. These tests therefore
/// use plain strings + a fake asset that reports a chosen generated class name; the correctness of
/// that precomputed name vs. <c>BlueprintIdHash</c>+<c>Sanitizer</c> is a blueprint-editor concern
/// (see <c>ComposedBlueprintDeleteBlockTests.BlueprintFileAsset_GeneratedClassName_*</c>, which
/// lives in the compiler-referencing part of this test project).
/// </para>
/// </summary>
public sealed class ComposedBlueprintResolverTests
{
    private const string ClassName = "ParamDemo_CEFE162F_Bp";
    private static string ComposedFqn(string className = ClassName, string method = "TickCore") =>
        $"{ComposedBlueprintResolver.GeneratedNamespace}.{className}.{method}";

    // ---- helpers ---------------------------------------------------------

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

    // A non-blueprint asset that (perversely) also reports an identity — used to prove the kind guard.
    private sealed class FakeNonBlueprintWithIdentity : IEditableAsset, IComposedBlueprintIdentity
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name { get; } = "Btree";
        public AssetKind Kind => AssetKind.BTree;
        public string SourceFilePath => string.Empty;
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

    // ---- TryParse ---------------------------------------------------------

    [Fact]
    public void TryParse_composed_fqn_extracts_class_and_method()
    {
        ComposedBlueprintResolver.TryParse(ComposedFqn(), out var generatedClassName, out var methodName)
            .Should().BeTrue();

        generatedClassName.Should().Be(ClassName);
        methodName.Should().Be("TickCore");
    }

    [Theory]
    [InlineData("Hrot.Game.Combat.CombatActions.AimAndFire")]        // ordinary hand-written action FQN
    [InlineData("AI.Actions.UseSpeed")]                              // short hand-written FQN
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_ignores_non_AiPrimitive_fqns(string? methodFqn)
    {
        ComposedBlueprintResolver.TryParse(methodFqn, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_rejects_fqn_missing_Bp_suffix()
    {
        // Trailing hex, but declaring type has no "_Bp" suffix.
        ComposedBlueprintResolver.TryParse("Ns.ParamDemo_CEFE162F.TickCore", out _, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TryParse_rejects_fqn_with_non_hex_suffix()
    {
        ComposedBlueprintResolver.TryParse("Ns.ParamDemo_ZZZZZZZZ_Bp.TickCore", out _, out _)
            .Should().BeFalse();
    }

    // ---- ElementKey / ReferenceKeyFor ------------------------------------

    [Fact]
    public void ReferenceKeyFor_matches_ElementKey_for_the_same_class()
    {
        var referenceKey = ComposedBlueprintResolver.ReferenceKeyFor(ComposedFqn());
        var elementKey    = ComposedBlueprintResolver.ElementKey(ClassName);

        referenceKey.Should().Be(elementKey);
        referenceKey.Should().Be($"{ClassName}.TickCore");
    }

    [Fact]
    public void ReferenceKeyFor_returns_null_for_non_composed_fqn()
    {
        ComposedBlueprintResolver.ReferenceKeyFor("Hrot.Game.Combat.CombatActions.AimAndFire").Should().BeNull();
    }

    // ---- Resolve ------------------------------------------------------------

    [Fact]
    public void Resolve_finds_the_real_matching_blueprint()
    {
        var blueprint = new FakeBlueprint { Name = "Param Demo", GeneratedClassName = ClassName };
        var catalog = new FakeCatalog(blueprint);

        var resolved = ComposedBlueprintResolver.Resolve(ComposedFqn(), catalog);

        resolved.Should().BeSameAs(blueprint);
    }

    [Fact]
    public void Resolve_returns_null_when_blueprint_was_deleted()
    {
        // Catalog is empty — nothing to match against.
        ComposedBlueprintResolver.Resolve(ComposedFqn(), new FakeCatalog()).Should().BeNull();
    }

    [Fact]
    public void Resolve_returns_null_when_blueprint_was_renamed()
    {
        // The blueprint still exists but now reports a DIFFERENT generated class name (its name, and
        // hence sanitized-name component, changed) — so the FQN's declaring type no longer matches.
        var renamed = new FakeBlueprint { GeneratedClassName = "SomethingElse_CEFE162F_Bp" };
        var catalog = new FakeCatalog(renamed);

        ComposedBlueprintResolver.Resolve(ComposedFqn(), catalog).Should().BeNull();
    }

    [Fact]
    public void Resolve_ignores_non_AiPrimitive_fqns()
    {
        var blueprint = new FakeBlueprint { GeneratedClassName = ClassName };
        var catalog = new FakeCatalog(blueprint);

        ComposedBlueprintResolver.Resolve("Hrot.Game.Combat.CombatActions.AimAndFire", catalog)
            .Should().BeNull();
    }

    [Fact]
    public void Resolve_ignores_non_Blueprint_catalog_entries_even_with_matching_identity()
    {
        // A non-Blueprint asset that reports the same generated class name must never satisfy the match.
        var notABlueprint = new FakeNonBlueprintWithIdentity { GeneratedClassName = ClassName };
        var catalog = new FakeCatalog(notABlueprint);

        ComposedBlueprintResolver.Resolve(ComposedFqn(), catalog).Should().BeNull();
    }

    [Fact]
    public void Resolve_returns_null_for_null_catalog()
    {
        ComposedBlueprintResolver.Resolve(ComposedFqn(), null).Should().BeNull();
    }
}
