using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Validation;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Validation;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Validation;

public class BTreeAssetValidatorTests
{
    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "test",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset(string name = "TestTree") =>
        new BehaviorTreeAsset(
            Guid.NewGuid(),
            name,
            "/trees/TestTree.cs",
            isEditorOwned: true,
            "MyBlackboard",
            "MyContext",
            EmptyBlob());

    [Fact]
    public void SupportedKind_IsBTree()
    {
        var validator = new BTreeAssetValidator(new BTreeValidator());
        validator.SupportedKind.Should().Be(AssetKind.BTree);
    }

    [Fact]
    public void Validate_WithWrongAssetKind_ReturnsEmpty()
    {
        var validator = new BTreeAssetValidator(new BTreeValidator());
        var stub = new StubEditableAsset(Guid.NewGuid(), "test", AssetKind.Hsm);
        var result = validator.Validate(stub);
        result.Should().BeEmpty();
    }

    // ── Behavioral tests ────────────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyComposite_YieldsEmptyCompositeDiagnostic()
    {
        // Arrange: Root → empty Sequence (Sequence has no children)
        var asset    = MakeAsset();
        var root     = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Root };
        var sequence = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Sequence };
        root.ChildVisualIds.Add(sequence.VisualId);
        asset.AddNode(root);
        asset.AddNode(sequence);

        var validator = new BTreeAssetValidator(new BTreeValidator());

        // Act
        var result = validator.Validate(asset);

        // Assert
        result.Should().Contain(d => d.Code == "EmptyComposite");
    }

    [Fact]
    public void Validate_UnboundAction_YieldsUnboundActionError()
    {
        // Arrange: Root → Action with no bound method
        var asset  = MakeAsset();
        var root   = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Root };
        var action = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Action };
        // Action payload is null → empty MethodFqn
        root.ChildVisualIds.Add(action.VisualId);
        asset.AddNode(root);
        asset.AddNode(action);

        var validator = new BTreeAssetValidator(new BTreeValidator());

        // Act
        var result = validator.Validate(asset);

        // Assert
        result.Should().Contain(d =>
            d.Code == "UnboundActionMethod" &&
            d.Severity == AssetDiagnosticSeverity.Error);
    }

    [Fact]
    public void Validate_ValidTree_NoEmptyCompositeOrUnboundError()
    {
        // Arrange: Root → Sequence → Action (with non-empty MethodFqn)
        var asset    = MakeAsset();
        var root     = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Root };
        var sequence = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Sequence };
        var action   = new BTreeEditorNode
        {
            VisualId   = Guid.NewGuid(),
            KernelType = NodeType.Action,
            Action     = new BTreeActionPayload { MethodFqn = "Hrot.Test.DoSomething" },
        };
        root.ChildVisualIds.Add(sequence.VisualId);
        sequence.ChildVisualIds.Add(action.VisualId);
        asset.AddNode(root);
        asset.AddNode(sequence);
        asset.AddNode(action);

        var validator = new BTreeAssetValidator(new BTreeValidator());

        // Act
        var result = validator.Validate(asset);

        // Assert
        result.Should().NotContain(d => d.Code == "EmptyComposite");
        result.Should().NotContain(d => d.Code == "UnboundActionMethod");
    }

    [Fact]
    public void Validate_PopulatesAssetIdAndName()
    {
        // Arrange: a tree that produces at least one diagnostic (empty composite)
        var asset    = MakeAsset("MyNamedTree");
        var root     = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Root };
        var sequence = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Sequence };
        root.ChildVisualIds.Add(sequence.VisualId);
        asset.AddNode(root);
        asset.AddNode(sequence);

        var validator = new BTreeAssetValidator(new BTreeValidator());

        // Act
        var result = validator.Validate(asset);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(d =>
        {
            d.AssetId.Should().Be(asset.AssetId);
            d.AssetName.Should().Be(asset.Name);
        });
    }
}

file sealed class StubEditableAsset : Hrot.Editor.AiShared.IEditableAsset
{
    public StubEditableAsset(Guid id, string name, AssetKind kind)
    { AssetId = id; Name = name; Kind = kind; }
    public Guid AssetId { get; }
    public string Name { get; }
    public AssetKind Kind { get; }
    public string SourceFilePath => "";
    public bool IsDirty => false;
    public bool IsEditorOwned => false;
    public event Action? Changed { add { } remove { } }
}
