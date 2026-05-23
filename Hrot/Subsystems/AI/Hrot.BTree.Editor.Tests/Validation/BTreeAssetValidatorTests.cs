using FluentAssertions;
using Hrot.BTree.Editor.Validation;
using Hrot.Editor.AiShared;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Validation;

public class BTreeAssetValidatorTests
{
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
