using System.Numerics;
using Fdp.Toolkit.Vis2D.Components;
using Xunit;

namespace Fdp.Presentation.Tests.Vis2D;

/// <summary>
/// ⭐ CE-1462 (W-B1) — the fit of a ground rectangle into the visible map, and when a terrain is framed: once per terrain, never otherwise, never a camera the user or a station owns.
/// Red-proofs (compile): <c>Fit</c> without the margin ⇒ the margin rail fails; <c>Framer.Step</c> not remembering <c>_framed</c> ⇒ the "never re-frames" rail fails.
/// </summary>
public sealed class MapFramingTests
{
    private static readonly Vector2 Area = new(1000, 600);

    [Fact]
    public void ASmallTerrain_ZoomsIn_TheWholeRectangleFitsWithTheMargin_CentredOnTheMiddle()
    {
        // cqb-village: 330 × 232 m in a 1000 × 600 px map
        var v = MapFraming.Fit(new Vector2(0, 0), new Vector2(330, 232), Area, Area / 2f, margin: 0.05f)!.Value;
        Assert.Equal(new Vector2(165, 116), v.Target);
        Assert.Equal(Area / 2f, v.Offset);
        Assert.Equal(v.Zoom, v.SmoothZoom);
        Assert.Equal(v.Target, v.SmoothTarget);
        float grow = 1.1f;
        Assert.True(330 * grow * v.Zoom <= 1000.001f && 232 * grow * v.Zoom <= 600.001f, "the rectangle plus its margin fits in the area");
        Assert.True(Math.Abs(330 * grow * v.Zoom - 1000f) < 0.01f || Math.Abs(232 * grow * v.Zoom - 600f) < 0.01f, "and it is the largest zoom that does (one dimension is tight)");
        Assert.True(v.Zoom > 2f);   // ≈ 2.75 px per metre: a small terrain is magnified, not left at the default
    }

    [Fact]
    public void ABigTerrain_ZoomsOut_ToTheLimit_AndTheMarginIsReal()
    {
        var big = MapFraming.Fit(new Vector2(-5000, -4000), new Vector2(5000, 4000), Area, Area / 2f, margin: 0.05f, minZoom: 0.1f)!.Value;   // 10 × 8 km: 0.0545 would fit, the camera floors at 0.1
        Assert.Equal(0.1f, big.Zoom);
        Assert.Equal(Vector2.Zero, big.Target);
        var mid = MapFraming.Fit(new Vector2(0, 0), new Vector2(2000, 1000), Area, Area / 2f, margin: 0.05f)!.Value;   // 2 × 1 km: 0.4545
        Assert.Equal(1000f / (2000f * 1.1f), mid.Zoom, 4);
        var noMargin = MapFraming.Fit(new Vector2(0, 0), new Vector2(2000, 1000), Area, Area / 2f, margin: 0f)!.Value;
        Assert.True(mid.Zoom < noMargin.Zoom);   // the margin takes room
    }

    [Fact]
    public void AnEmptyRectangleOrArea_FitsNothing()
    {
        Assert.Null(MapFraming.Fit(Vector2.Zero, Vector2.Zero, Area, Area / 2f));
        Assert.Null(MapFraming.Fit(new Vector2(5, 5), new Vector2(1, 9), Area, Area / 2f));
        Assert.Null(MapFraming.Fit(Vector2.Zero, new Vector2(10, 10), Vector2.Zero, Area / 2f));
        Assert.Null(MapFraming.Fit(Vector2.Zero, new Vector2(float.NaN, 10), Area, Area / 2f));
    }

    [Fact]
    public void TheFramer_FiresOnTheFirstResidentTerrain_AndAgainOnAChange_NeverOtherwise()
    {
        var f = new MapFraming.Framer();
        Assert.False(f.Step(null, false, false, false));                  // nothing loaded
        Assert.False(f.Step("cqb-village", false, false, false));         // named but not drawable yet: wait
        Assert.True(f.Step("cqb-village", true, false, false));           // the first frame it is resident and drawable: frame
        for (int i = 0; i < 5; i++) Assert.False(f.Step("cqb-village", true, false, false));   // the user's pans and zooms win
        Assert.True(f.Step("demo-city", true, false, false));             // a different terrain: frame again
        Assert.False(f.Step("demo-city", true, false, false));
        Assert.False(f.Step(null, false, false, false));                  // an unload forgets it ...
        Assert.True(f.Step("demo-city", true, false, false));             // ... so the next load frames
    }

    [Fact]
    public void TheFramer_LeavesAFollowedOrDrivenOr3DCameraAlone_AndDoesNotFrameItLater()
    {
        var f = new MapFraming.Framer();
        Assert.False(f.Step("a", true, switching: true, leaveAlone: false));   // mid-switch: retry, not consumed
        Assert.False(f.Step("a", true, false, leaveAlone: true));              // the camera is not the 2-D map's: consumed unframed
        Assert.False(f.Step("a", true, false, leaveAlone: false));             // back on the 2-D map: the user's view, not taken behind their back
        Assert.True(f.Step("b", true, false, leaveAlone: false));              // a NEW terrain frames
    }
}
