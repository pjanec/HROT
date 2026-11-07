using System;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Editing;

/// <summary>
/// ⭐ U-10 / M1 (docs/UX/DESIGN_Show_On_Map.md §2, Q-M1…Q-M4) — show on map: the TYPE decides (<see cref="MapPreviews.Has"/>); hover draws for one
/// frame; click pins; a pin lives while its row is drawn (<see cref="MapPreview.Keep"/>), so a selection change clears it; one teal on the
/// never-filmed tool-handles layer; 0 B on a steady frame. 🔴 Red-proof (compiles): <c>Emit</c> not dropping un-kept pins ⇒ the selection rail fails.
/// </summary>
public sealed class MapPreviewTests
{
    private static readonly Fdp.Core.FixedString32 L = new("Position");

    private static int Count(DebugPrimitiveBuffer b, DebugPrimitiveShape shape)
    {
        int n = 0;
        foreach (ref readonly var p in b.GetFrame()) if (p.Shape == shape) { n++; Assert.Equal(MapPreviews.Layer, p.DebugLayer); }
        return n;
    }

    [Fact]
    public void M1_TheTypeDecides()
    {
        Assert.True(MapPreviews.Has(typeof(WorldPoint)));
        Assert.True(MapPreviews.Has(typeof(PointLink)));
        Assert.True(MapPreviews.Has(typeof(EntityLink)));
        Assert.True(MapPreviews.Has(typeof(EntityRef)));
        Assert.True(MapPreviews.Has(typeof(AreaLink)));
        Assert.False(MapPreviews.Has(typeof(System.Numerics.Vector3)));   // Q-M3: a plain vector is not a place (a velocity would get a ⌖)
        Assert.False(MapPreviews.Has(typeof(float)));
    }

    [Fact]
    public void M1_Hover_DrawsOneFrame_ClickPins_UntilClickedAgain()
    {
        var mp = new MapPreview();
        var buf = new DebugPrimitiveBuffer();
        var owner = new object();
        object p = new WorldPoint(10, 20, 0);

        mp.Hover(p, L); mp.Emit(buf, null);
        Assert.Equal(1, Count(buf, DebugPrimitiveShape.Sphere));   // a ring + its label
        Assert.Equal(1, Count(buf, DebugPrimitiveShape.Text));
        buf.Clear(); mp.Emit(buf, null);
        Assert.Equal(0, buf.Count);                                // hover is this frame only

        mp.TogglePin(owner, "$.Where", p, L);
        for (int f = 0; f < 3; f++) { buf.Clear(); mp.Keep(owner, "$.Where", p); mp.Emit(buf, null); Assert.Equal(1, Count(buf, DebugPrimitiveShape.Sphere)); }
        mp.TogglePin(owner, "$.Where", p, L);
        buf.Clear(); mp.Emit(buf, null);
        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void M1_ASelectionChange_ClearsThePins_TheRowIsGone()
    {
        var mp = new MapPreview();
        var buf = new DebugPrimitiveBuffer();
        var owner = new object();
        mp.TogglePin(owner, "$.Where", new WorldPoint(1, 2, 0), L);
        mp.Emit(buf, null);
        buf.Clear(); mp.Emit(buf, null);   // the row was not drawn this frame (another unit selected)
        buf.Clear(); mp.Emit(buf, null);
        Assert.Equal(0, mp.PinCount);
        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void M1_AnArea_IsItsDashedOutline_ARectangleFourEdges()
    {
        var buf = new DebugPrimitiveBuffer();
        Assert.True(MapPreviews.Draw(buf, AreaLink.Rectangle(new WorldPoint(0, 0, 0), 20, 40, new Bearing(30)), null, L));
        Assert.Equal(4, Count(buf, DebugPrimitiveShape.Line));
        foreach (ref readonly var p in buf.GetFrame()) if (p.Shape == DebugPrimitiveShape.Line) Assert.Equal(LineStyle.Dashed, p.LineStyle);
        buf.Clear();
        Assert.True(MapPreviews.Draw(buf, AreaLink.Circle(new WorldPoint(5, 5, 0), 30), null, L));
        Assert.Equal(1, Count(buf, DebugPrimitiveShape.Sphere));
        buf.Clear();
        Assert.False(MapPreviews.Draw(buf, new EntityLink(new EntityRef(7), default), null, L));   // no scope ⇒ no place known ⇒ nothing drawn
        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void M1_ASteadyFrameWithAPin_AllocatesNothing()
    {
        var mp = new MapPreview();
        var buf = new DebugPrimitiveBuffer();
        var owner = new object();
        object p = new WorldPoint(3, 4, 0);
        mp.TogglePin(owner, "$.Where", p, L);
        for (int f = 0; f < 3; f++) { buf.Clear(); mp.Keep(owner, "$.Where", p); mp.Hover(p, L); mp.Emit(buf, null); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 50; f++) { buf.Clear(); mp.Keep(owner, "$.Where", p); mp.Hover(p, L); mp.Emit(buf, null); }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
