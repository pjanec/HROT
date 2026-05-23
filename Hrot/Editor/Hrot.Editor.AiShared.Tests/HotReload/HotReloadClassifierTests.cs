using Hrot.Editor.AiShared.HotReload;

namespace Hrot.Editor.AiShared.Tests.HotReload;

public sealed class HotReloadClassifierTests
{
    [Fact]
    public void Classify_WhenStructureHashChanged_ReturnsHard()
    {
        var tier = HotReloadClassifier.Classify(1, 2, 10, 10);
        Assert.Equal(HotReloadTier.Hard, tier);
    }

    [Fact]
    public void Classify_WhenOnlyParamHashChanged_ReturnsSoft()
    {
        var tier = HotReloadClassifier.Classify(5, 5, 10, 20);
        Assert.Equal(HotReloadTier.Soft, tier);
    }

    [Fact]
    public void Classify_WhenNeitherHashChanged_ReturnsCosmetic()
    {
        var tier = HotReloadClassifier.Classify(5, 5, 10, 10);
        Assert.Equal(HotReloadTier.Cosmetic, tier);
    }

    [Fact]
    public void Classify_WhenBothHashesChanged_ReturnsHard()
    {
        // Structure dominates when both hashes differ.
        var tier = HotReloadClassifier.Classify(1, 99, 10, 99);
        Assert.Equal(HotReloadTier.Hard, tier);
    }

    [Fact]
    public void Classify_SameStructure_SameParam_Cosmetic()
    {
        var tier = HotReloadClassifier.Classify(0, 0, 0, 0);
        Assert.Equal(HotReloadTier.Cosmetic, tier);
    }

    [Fact]
    public void MostImpactful_HardAndSoft_ReturnsHard()
    {
        Assert.Equal(HotReloadTier.Hard, HotReloadClassifier.MostImpactful(HotReloadTier.Hard, HotReloadTier.Soft));
        Assert.Equal(HotReloadTier.Hard, HotReloadClassifier.MostImpactful(HotReloadTier.Soft, HotReloadTier.Hard));
    }

    [Fact]
    public void MostImpactful_SoftAndCosmetic_ReturnsSoft()
    {
        Assert.Equal(HotReloadTier.Soft, HotReloadClassifier.MostImpactful(HotReloadTier.Soft, HotReloadTier.Cosmetic));
        Assert.Equal(HotReloadTier.Soft, HotReloadClassifier.MostImpactful(HotReloadTier.Cosmetic, HotReloadTier.Soft));
    }

    [Fact]
    public void MostImpactful_TwoCosmetics_ReturnsCosmetic()
    {
        Assert.Equal(HotReloadTier.Cosmetic, HotReloadClassifier.MostImpactful(HotReloadTier.Cosmetic, HotReloadTier.Cosmetic));
    }

    [Fact]
    public void HotReloadStatus_Hard_WithInstances_RequiresConfirmation()
    {
        var status = new HotReloadStatus(HotReloadTier.Hard, 5);
        Assert.True(status.RequiresConfirmation);
    }

    [Fact]
    public void HotReloadStatus_Hard_NoInstances_DoesNotRequireConfirmation()
    {
        var status = new HotReloadStatus(HotReloadTier.Hard, 0);
        Assert.False(status.RequiresConfirmation);
    }

    [Fact]
    public void HotReloadStatus_Soft_DoesNotRequireConfirmation()
    {
        var status = new HotReloadStatus(HotReloadTier.Soft, 10);
        Assert.False(status.RequiresConfirmation);
    }
}
