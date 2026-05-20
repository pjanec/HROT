namespace Hrot.Blueprints.Tests;

public sealed class SampleAssetLoadTests
{
    public static IEnumerable<object[]> AllSampleNames =>
        new[]
        {
            new object[] { TestData.SampleAssets.LibraryMath },
            new object[] { TestData.SampleAssets.InstanceCounter },
            new object[] { TestData.SampleAssets.InstanceCounterV1ModifiedBody },
            new object[] { TestData.SampleAssets.InstanceCounterV2WithBonus },
            new object[] { TestData.SampleAssets.HealthRegen },
            new object[] { TestData.SampleAssets.HasVisibleTarget },
            new object[] { TestData.SampleAssets.MoveToAndFire },
            new object[] { TestData.SampleAssets.DoorActor },
            new object[] { TestData.SampleAssets.DoorSensor },
        };

    [Theory]
    [MemberData(nameof(AllSampleNames))]
    public void LoadAsset_ValidSamples_ParseWithoutException(string name)
    {
        var asset = TestData.LoadAsset(name);
        Assert.NotNull(asset);
        Assert.NotEmpty(asset.Name);
    }

    [Fact]
    public void LoadAsset_InvalidConditionWithRunning_ParsesOk()
    {
        // Semantically invalid but syntactically valid -- should parse fine.
        var asset = TestData.LoadAsset("Invalid/ConditionWithRunning");
        Assert.NotNull(asset);
    }

    [Fact]
    public void LoadSnapshot_NonExistentSnapshot_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(
            () => TestData.LoadSnapshot("Schedule/LibraryMath.ir.txt"));
    }
}
