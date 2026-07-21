using Hrot.Editor.AiShared;

namespace Hrot.Editor.AiShared.Tests.Identity;

/// <summary>
/// Punch-list #9: <see cref="AssetKindIcons.ResolveIconKey"/> prefers an
/// <see cref="IAssetIconKeyProvider"/> override and otherwise falls back to the per-kind icon —
/// the single choke point both the Open-Asset picker and the asset-browser panel route through.
/// </summary>
public sealed class AssetIconKeyOverrideTests
{
    private sealed class FakeAsset : IEditableAsset, IAssetIconKeyProvider
    {
        public Guid AssetId { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "x";
        public AssetKind Kind { get; set; } = AssetKind.Blueprint;
        public string SourceFilePath { get; set; } = "";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
        public string? IconKey { get; set; }
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    [Fact]
    public void ResolveIconKey_UsesOverride_WhenProvided()
        => Assert.Equal("asset/blueprint_condition",
            AssetKindIcons.ResolveIconKey(new FakeAsset { IconKey = "asset/blueprint_condition" }));

    [Fact]
    public void ResolveIconKey_FallsBackToKind_WhenOverrideNullOrEmpty()
    {
        Assert.Equal(AssetKindIcons.GetIconKey(AssetKind.Blueprint),
            AssetKindIcons.ResolveIconKey(new FakeAsset { IconKey = null }));
        Assert.Equal(AssetKindIcons.GetIconKey(AssetKind.Blueprint),
            AssetKindIcons.ResolveIconKey(new FakeAsset { IconKey = "" }));
    }
}
