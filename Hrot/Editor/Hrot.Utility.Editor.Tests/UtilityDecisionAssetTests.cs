using System;
using Fdp.Toolkit.Utility;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Selection;
using Hrot.Utility.Editor.Model;
using Hrot.Utility.Editor.Tracing;
using Hrot.Utility.Editor.Windows;
using Xunit;

namespace Hrot.Utility.Editor.Tests;

public class UtilityDecisionAssetTests
{
    // ---- UtilityDecisionAsset -------------------------------------------

    [Fact]
    public void Kind_Returns_Utility()
    {
        var asset = new UtilityDecisionAsset();
        Assert.Equal(AssetKind.Utility, asset.Kind);
    }

    [Fact]
    public void Name_Returns_DisplayName()
    {
        var asset = new UtilityDecisionAsset { DisplayName = "TestDecision" };
        Assert.Equal("TestDecision", asset.Name);
    }

    [Fact]
    public void SetIsDirty_True_FiresChangedEvent()
    {
        var asset = new UtilityDecisionAsset();
        int count = 0;
        asset.Changed += () => count++;

        asset.IsDirty = true;

        Assert.Equal(1, count);
    }

    [Fact]
    public void SetIsDirty_True_Twice_FiresChangedOnce()
    {
        var asset = new UtilityDecisionAsset();
        int count = 0;
        asset.Changed += () => count++;

        asset.IsDirty = true;
        asset.IsDirty = true;

        Assert.Equal(1, count);
    }

    [Fact]
    public void SetIsDirty_False_AfterTrue_DoesNotRefire()
    {
        var asset = new UtilityDecisionAsset();
        int count = 0;
        asset.Changed += () => count++;

        asset.IsDirty = true;
        asset.IsDirty = false;

        Assert.Equal(1, count);
    }

    [Fact]
    public void IsEditorOwned_ReflectsWhatWasSet()
    {
        var asset = new UtilityDecisionAsset { IsEditorOwned = true };
        Assert.True(asset.IsEditorOwned);

        asset.IsEditorOwned = false;
        Assert.False(asset.IsEditorOwned);
    }

    // ---- ResponseCurveModel ---------------------------------------------

    [Fact]
    public void ToRuntime_Returns_Correct_Kind_M_K_B()
    {
        var model = new ResponseCurveModel
        {
            Kind = CurveKind.Quadratic,
            M    = 0.5f,
            K    = 2f,
            B    = 0.1f,
            C    = 0.3f,
        };
        var runtime = model.ToRuntime();

        Assert.Equal(CurveKind.Quadratic, runtime.Kind);
        Assert.Equal(0.5f, runtime.Slope);
        Assert.Equal(2f,   runtime.Exponent);
        Assert.Equal(0.1f, runtime.XShift);
    }

    // ---- UtilityTraceLaneProvider ---------------------------------------

    [Fact]
    public void TraceLaneProvider_Kind_Is_Utility()
    {
        var provider = new UtilityTraceLaneProvider();
        Assert.Equal(AssetKind.Utility, provider.Kind);
    }

    [Fact]
    public void TraceLaneProvider_Lanes_HasExactlyTwo()
    {
        var provider = new UtilityTraceLaneProvider();
        Assert.Equal(2, provider.Lanes.Count);
    }

    [Fact]
    public void TraceLaneProvider_LaneIds_AreCorrect()
    {
        var provider = new UtilityTraceLaneProvider();
        Assert.Equal("utility_scoring", provider.Lanes[0].Id);
        Assert.Equal("utility_values",  provider.Lanes[1].Id);
    }

    // ---- UtilityDecisionWindow ------------------------------------------

    [Fact]
    public void ActiveAsset_IsNull_BeforeOpenAsset()
    {
        var store  = new EditorSelectionStore();
        var window = new UtilityDecisionWindow(store);

        Assert.Null(window.ActiveAsset);
    }

    [Fact]
    public void OpenAsset_SetsActiveAsset_And_IsOpen()
    {
        var store  = new EditorSelectionStore();
        var window = new UtilityDecisionWindow(store);
        var asset  = new UtilityDecisionAsset { DisplayName = "MyDecision" };

        window.OpenAsset(asset);

        Assert.Same(asset, window.ActiveAsset);
        Assert.True(window.IsOpen);
    }
}
