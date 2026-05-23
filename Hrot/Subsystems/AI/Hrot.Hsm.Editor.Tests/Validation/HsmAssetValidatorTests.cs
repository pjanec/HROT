using FluentAssertions;
using Hrot.Editor.AiShared;
using Hrot.Hsm.Editor.Validation;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Validation;

public class HsmAssetValidatorTests
{
    [Fact]
    public void SupportedKind_IsHsm()
    {
        var validator = new HsmAssetValidator(new HsmValidator());
        validator.SupportedKind.Should().Be(AssetKind.Hsm);
    }

    [Fact]
    public void Validate_WithWrongAssetKind_ReturnsEmpty()
    {
        var validator = new HsmAssetValidator(new HsmValidator());
        var stub = new StubEditableAsset(Guid.NewGuid(), "test", AssetKind.BTree);
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
