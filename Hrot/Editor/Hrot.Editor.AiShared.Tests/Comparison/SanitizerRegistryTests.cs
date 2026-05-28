using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class SanitizerRegistryTests
{
    private sealed class FakeSanitizer : IAssetComparisonSanitizer
    {
        public AssetKind TargetKind { get; init; }
        public SanitizationResult Sanitize(AssetExportRequest request) =>
            throw new NotImplementedException();
    }

    [Fact]
    public void Register_ThenGet_ReturnsSameSanitizer()
    {
        var registry = new SanitizerRegistry();
        var sanitizer = new FakeSanitizer { TargetKind = AssetKind.BTree };
        registry.Register(sanitizer);

        var result = registry.Get(AssetKind.BTree);

        Assert.Same(sanitizer, result);
    }

    [Fact]
    public void Get_UnregisteredKind_ThrowsWithKindNameInMessage()
    {
        var registry = new SanitizerRegistry();

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Get(AssetKind.Hsm));

        Assert.Contains("Hsm", ex.Message);
    }

    [Fact]
    public void TryGet_UnregisteredKind_ReturnsFalseAndNull()
    {
        var registry = new SanitizerRegistry();

        bool found = registry.TryGet(AssetKind.Blueprint, out var sanitizer);

        Assert.False(found);
        Assert.Null(sanitizer);
    }

    [Fact]
    public void TryGet_RegisteredKind_ReturnsTrueAndSanitizer()
    {
        var registry = new SanitizerRegistry();
        var sanitizer = new FakeSanitizer { TargetKind = AssetKind.BTree };
        registry.Register(sanitizer);

        bool found = registry.TryGet(AssetKind.BTree, out var result);

        Assert.True(found);
        Assert.Same(sanitizer, result);
    }

    [Fact]
    public void Register_DoubleRegistration_SecondOverwritesFirst()
    {
        var registry = new SanitizerRegistry();
        var first  = new FakeSanitizer { TargetKind = AssetKind.BTree };
        var second = new FakeSanitizer { TargetKind = AssetKind.BTree };
        registry.Register(first);
        registry.Register(second);

        var result = registry.Get(AssetKind.BTree);

        Assert.Same(second, result);
    }
}
