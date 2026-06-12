using FluentAssertions;
using Fbt;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.BTree.Editor;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Persistence;

/// <summary>
/// BATCH-06 structural tests for the CombatShowcase.btree.json showcase asset
/// and the in-code "Starter" recipe (Decision D-03).
///
/// Note: runtime FQN/registration resolution is confirmed at REVIEW-BT (editor load),
/// not in these structural tests.
/// </summary>
public sealed class BTreeShowcaseAndStarterTests : IDisposable
{
    private readonly string _tempRoot;

    public BTreeShowcaseAndStarterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BTreeShowcaseAndStarterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Path resolution ───────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the committed CombatShowcase.btree.json path by walking up from the
    /// test assembly output directory to the repo root, then into the Behaviors assets tree.
    /// Mirrors the pattern used by SampleScoutDiscoveryTests.
    /// </summary>
    private static string ResolveShowcasePath()
    {
        var asmDir = Path.GetDirectoryName(typeof(BTreeShowcaseAndStarterTests).Assembly.Location)!;
        var repoRoot = asmDir;
        for (int i = 0; i < 7; i++)
            repoRoot = Path.GetDirectoryName(repoRoot)!;
        return Path.Combine(repoRoot, "Hrot", "Subsystems", "Hrot.AI.Behaviors",
            "Assets", "BTrees", "CombatShowcase.btree.json");
    }

    // ── Showcase: deserialization ─────────────────────────────────────────────

    [Fact]
    public void Showcase_Deserializes()
    {
        var path = ResolveShowcasePath();
        File.Exists(path).Should().BeTrue(
            $"CombatShowcase.btree.json must exist at {path}");

        var text = File.ReadAllText(path);
        var dto = BTreeJsonServices.Deserialize(text);

        dto.Should().NotBeNull("showcase JSON must deserialize to a valid DTO");
        dto!.Name.Should().Be("CombatShowcase");
        dto.Nodes.Should().NotBeEmpty("showcase must have nodes");
        dto.Pills.Should().NotBeEmpty("showcase must have decorator pills");
    }

    // ── Showcase: round-trip byte stability ───────────────────────────────────

    [Fact]
    public void Showcase_RoundTripByteStable()
    {
        var path = ResolveShowcasePath();
        var text = File.ReadAllText(path);

        // First round: deserialize → serialize
        var dto1 = BTreeJsonServices.Deserialize(text);
        dto1.Should().NotBeNull();
        var ser1 = BTreeJsonServices.Serialize(dto1!);

        // Second round: deserialize → serialize again
        var dto2 = BTreeJsonServices.Deserialize(ser1);
        dto2.Should().NotBeNull();
        var ser2 = BTreeJsonServices.Serialize(dto2!);

        // Serialized output must be idempotent: serialize(deserialize(x)) == serialize(deserialize(serialize(deserialize(x))))
        ser2.Should().Be(ser1,
            "BTreeJsonServices round-trip must be byte-stable (serialize→deserialize→serialize is idempotent)");
    }

    // ── Showcase: projection has all required features ────────────────────────

    [Fact]
    public void Showcase_Projects_HasAllFeatures()
    {
        var path = ResolveShowcasePath();
        var text = File.ReadAllText(path);
        var dto = BTreeJsonServices.Deserialize(text);
        dto.Should().NotBeNull();

        var asset = BehaviorTreeAssetMapper.FromDto(dto!);
        asset.Should().NotBeNull();

        // ── ObserverSelector node ──────────────────────────────────────────
        asset.Nodes.Should().Contain(n => n.KernelType == NodeType.ObserverSelector,
            "must contain an ObserverSelector node (eye glyph + OBSERVES badge)");

        var observerSel = asset.Nodes.First(n => n.KernelType == NodeType.ObserverSelector);
        observerSel.ChildVisualIds.Should().HaveCount(1,
            "ObserverSelector must have one child (Sequence)");

        // ── Action leaf with non-empty MethodFqn ───────────────────────────
        asset.Nodes.Should().Contain(n => n.KernelType == NodeType.Action,
            "must contain an Action leaf");
        var actNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);
        actNode.Action.Should().NotBeNull();
        actNode.Action!.MethodFqn.Should().NotBeNullOrEmpty(
            "Action leaf must have a non-empty MethodFqn");
        actNode.Action.MethodFqn.ToLowerInvariant().Should().Contain("action",
            "Action leaf MethodFqn must contain 'Action' (real [BTreeAction])");

        // ── Two pills on the Action node (Repeater + Cooldown) ─────────────
        var actionVisualId = actNode.VisualId;
        var actionPills = asset.Pills
            .Where(p => p.HostNodeVisualId == actionVisualId)
            .OrderBy(p => p.StackIndex)
            .ToList();

        actionPills.Should().HaveCount(2,
            "Action node must carry 2 stacked decorator pills");

        actionPills[0].DecoratorType.Should().Be(NodeType.Repeater,
            "first pill (StackIndex 0) must be Repeater");
        actionPills[0].IntParam.Should().Be(3,
            "Repeater pill must have IntParam = 3");
        actionPills[0].StackIndex.Should().Be(0);

        actionPills[1].DecoratorType.Should().Be(NodeType.Cooldown,
            "second pill (StackIndex 1) must be Cooldown");
        actionPills[1].FloatParam.Should().Be(2.0f,
            "Cooldown pill must have FloatParam = 2.0");
        actionPills[1].StackIndex.Should().Be(1);

        // ── Wait leaf ─────────────────────────────────────────────────────
        asset.Nodes.Should().Contain(n => n.KernelType == NodeType.Wait,
            "must contain a Wait leaf");
        var waitNode = asset.Nodes.First(n => n.KernelType == NodeType.Wait);
        waitNode.Wait.Should().NotBeNull();
        waitNode.Wait!.Duration.Should().Be(1.5f);

        // ── Subtree leaf referencing SampleScout ───────────────────────────
        asset.Nodes.Should().Contain(n => n.KernelType == NodeType.Subtree,
            "must contain a Subtree leaf");
        var subtreeNode = asset.Nodes.First(n => n.KernelType == NodeType.Subtree);
        subtreeNode.Subtree.Should().NotBeNull();
        subtreeNode.Subtree!.SubtreeName.Should().Be("SampleScout",
            "Subtree must reference SampleScout by name");
        subtreeNode.Subtree.SubtreeAssetId.Should().NotBe(Guid.Empty,
            "SubtreeAssetId must be non-empty (SampleScout's AssetId)");
        subtreeNode.Subtree.IsResolved.Should().BeTrue(
            "Subtree must be marked as resolved");
    }

    // ── Starter: in AvailableRecipes ─────────────────────────────────────────

    [Fact]
    public void Starter_InAvailableRecipes()
    {
        var svc = new BTreeNewAssetService(_tempRoot);
        var recipes = svc.AvailableRecipes();

        recipes.Should().Contain(r => r.Name == "Starter",
            "AvailableRecipes() must contain a 'Starter' entry (Decision D-03)");

        var starter = recipes.First(r => r.Name == "Starter");
        starter.Should().NotBeNull();
        starter.Name.Should().Be("Starter");
    }

    [Fact]
    public void Starter_InAvailableRecipes_HasCorrectKind()
    {
        var svc = new BTreeNewAssetService(_tempRoot);
        var recipes = svc.AvailableRecipes();
        var starter = recipes.First(r => r.Name == "Starter");

        starter.Kind.Should().Be(Hrot.Editor.AiShared.AssetKind.BTree);
    }

    // ── Starter: CreateNew yields Root + Sequence ────────────────────────────

    [Fact]
    public void Starter_CreateNew_YieldsRootPlusSequence()
    {
        var svc = new BTreeNewAssetService(_tempRoot);
        var recipes = svc.AvailableRecipes();
        var starter = recipes.First(r => r.Name == "Starter");

        // CreateNew clones the recipe — it writes a file to the temp root.
        var result = svc.CreateNew(starter, "MyNew", "");

        var expectedPath = Path.Combine(_tempRoot, "MyNew.btree.json");
        File.Exists(expectedPath).Should().BeTrue(
            $"CreateNew must write the cloned asset to {expectedPath}");

        // Deserialize the written file.
        var json = File.ReadAllText(expectedPath);
        var dto = BTreeJsonServices.Deserialize(json);
        dto.Should().NotBeNull();
        dto!.Name.Should().Be("MyNew");
        dto!.AssetId.Should().NotBe(Guid.Empty);
        // Must be a different ID than the recipe's (cloning gives a fresh identity).
        dto!.AssetId.Should().NotBe(starter.AssetId,
            "cloned asset must have a fresh AssetId");

        // Must contain exactly a Root node and a Sequence node.
        dto!.Nodes.Should().HaveCount(2,
            "Starter tree must have exactly 2 nodes: Root + Sequence");

        var rootNode = dto.Nodes.OfType<BTreeRootNodeDto>().FirstOrDefault();
        rootNode.Should().NotBeNull("Starter must have a Root node");
        rootNode!.ChildVisualIds.Should().HaveCount(1,
            "Root must have exactly one child (the Sequence)");

        var seqNode = dto.Nodes.OfType<BTreeSequenceNodeDto>().FirstOrDefault();
        seqNode.Should().NotBeNull("Starter must have a Sequence node");
        seqNode!.ChildVisualIds.Should().BeEmpty(
            "Sequence must start empty (no children)");

        // The Root's child must point to the Sequence.
        rootNode.ChildVisualIds[0].Should().Be(seqNode.VisualId,
            "Root's child must be the Sequence node");

        // Returned adapter has correct metadata.
        result.Name.Should().Be("MyNew");
        result.SourceFilePath.Should().Be(expectedPath);
    }

    // ── Starter: recipe DTO is inspectable via adapter ───────────────────────

    [Fact]
    public void Starter_RecipeDto_IsInspectable()
    {
        var svc = new BTreeNewAssetService(_tempRoot);
        var recipes = svc.AvailableRecipes();
        var starter = recipes.First(r => r.Name == "Starter");

        // The adapter must carry a non-null DTO so callers can inspect recipe content.
        var adapter = starter.Should().BeOfType<BTreeEditableAssetAdapter>().Subject;
        adapter.Dto.Should().NotBeNull("Starter recipe adapter must carry its DTO");
        adapter.Dto!.Name.Should().Be("Starter");
        adapter.Dto!.Nodes.Should().HaveCount(2);
    }

    // ── Empty recipe still present ───────────────────────────────────────────

    [Fact]
    public void Empty_Recipe_StillPresent()
    {
        var svc = new BTreeNewAssetService(_tempRoot);
        var recipes = svc.AvailableRecipes();

        recipes.Should().Contain(r => r.Name == "Empty",
            "the 'Empty' recipe must still be available alongside 'Starter'");
    }
}
