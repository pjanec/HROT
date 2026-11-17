using System;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Interfaces;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐ CE-8610 fidelity (R-4146; mockups <c>docs/designs/aggregate-units/agg-ui-visuals.svg</c> ③ ⑥ and <c>docs/UX/unit-symbols-visual.svg</c> A / C) — the REAL
/// gizmos on the REAL map: a friend infantry platoon's unit card with its derived symbol as the LEFT CELL, and a hostile tank platoon's symbol ALONE at its
/// point. Measured: the friend fill sits in the card's left part (left of the card's anchor, above it) and the card's name starts right of the cell; the
/// hostile fill sits on the tank platoon's point. Saved under <c>HROT_RAIL_SHOTS</c> (⛔ never committed); skips without a display (<c>xvfb-run -a</c>).
/// 🔴 Red-proof (compiles): the rows not shifted past the cell ⇒ the name's first pixels land in the cell ⇒ red.
/// </summary>
[Collection(Hrot.Presentation.Tests.Map3D.Map3DFrameRail.Serial)]
public sealed class UnitSymbolFrameRail
{
    private const int W = 700, H = 420;
    private const float Zoom = 1.6f;
    private static readonly Vector2 View = new(200f, 100f);
    private static readonly Color Ground = new(60, 90, 50, 255);

    private static Vector2 Screen(Vector2 world) => new((world.X - (View.X - W / 2f / Zoom)) * Zoom, ((View.Y + H / 2f / Zoom) - world.Y) * Zoom);

    [SkippableFact]
    public void CE8610_AUnitCardCarriesItsSymbolAsTheLeftCell_AndAnotherUnitsSymbolStandsAlone()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        Fdp.Presentation.Fonts.GizmoFont.Load();
        var canvas = new MapCanvas(input: null);
        var camera = new MapCamera();
        var target = new Vector2(View.X - W / 2f / Zoom, View.Y + H / 2f / Zoom);
        camera.ApplyCameraView(new MapCameraView { Target = target, Zoom = Zoom, SmoothTarget = target, SmoothZoom = Zoom });
        canvas.Camera = camera;
        var buffer = new DebugPrimitiveBuffer();
        var layer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus(), camera: camera);
        canvas.AddLayer(layer);

        using var w = new EntityRepository();
        w.RegisterComponent<NetworkIdentity>(); w.RegisterComponent<EntityInfo>(); w.RegisterComponent<UnitEntity>(); w.RegisterComponent<TkbIdentity>();
        var db = new TkbDatabase();
        NedTkbCatalog.RegisterAll(db);
        w.SetSingletonManaged<ITkbDatabase>(db);
        Entity Unit(long type, int net, string name, ForceId side)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new NetworkIdentity(net)); w.AddComponent(e, new UnitEntity()); w.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            w.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
            return e;
        }
        var plt = Unit(TkbEntityTypes.Unit_InfantryPlatoon, 81, "2 Plt", ForceId.Friend);
        var tanks = Unit(TkbEntityTypes.Unit_TankPlatoon, 82, "T Plt", ForceId.Hostile);
        Vector2 pltAt = new(110, 60), tanksAt = new(300, 110);

        buffer.DrawBox2D(View, new Vector2(3000f, 3000f), new Rgba32(Ground.R, Ground.G, Ground.B, 255), 0f, 0f, SizeMode.WorldMeters, fillColor: new Rgba32(Ground.R, Ground.G, Ground.B, 255));
        buffer.DrawSpatialAnchor(81, pltAt.X, pltAt.Y, 0f, 0f);
        buffer.DrawSpatialAnchor(82, tanksAt.X, tanksAt.Y, 0f, 0f);
        new EntityCardFrameGizmo().Draw(w, plt, buffer);
        new EntityNameGizmo().Draw(w, plt, buffer);
        new UnitSymbolCardGizmo().Draw(w, plt, buffer);
        new UnitSymbolPlatoonGizmo().Draw(w, tanks, buffer);
        layer.Update(0.016f);
        frame.StepN(3, canvas.Draw);
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-agg-look");
        Directory.CreateDirectory(dir);
        string png = Path.Combine(dir, "agg-symbol-cells.png");
        frame.Screenshot(png);

        var image = Raylib.LoadImage(png);
        try
        {
            static bool Friend(Color c) => Math.Abs(c.R - 128) < 14 && Math.Abs(c.G - 224) < 14 && Math.Abs(c.B - 255) < 14;
            static bool Hostile(Color c) => Math.Abs(c.R - 255) < 14 && Math.Abs(c.G - 128) < 14 && Math.Abs(c.B - 128) < 14;
            static bool White(Color c) => c.R > 235 && c.G > 235 && c.B > 235;
            var a = Screen(pltAt); var t = Screen(tanksAt);
            int friendLeft = 0, friendRight = 0, friendMaxX = 0, whiteMinX = int.MaxValue, hostileNear = 0;
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    var c = Raylib.GetImageColor(image, x, y);
                    bool aboveAnchor = y < a.Y && y > a.Y - 90;
                    if (Friend(c) && aboveAnchor) { if (x < a.X) friendLeft++; else friendRight++; friendMaxX = Math.Max(friendMaxX, x); }
                    if (Hostile(c) && Vector2.Distance(new Vector2(x, y), t) < 20f) hostileNear++;
                    if (White(c) && aboveAnchor && Math.Abs(x - a.X) < 120) whiteMinX = Math.Min(whiteMinX, x);
                }
            Assert.True(friendLeft > 150, $"the friend frame fills the card's LEFT cell ({friendLeft} px left of the anchor, {friendRight} right)");
            Assert.True(friendLeft > 3 * friendRight, $"the cell is on the left ({friendLeft} vs {friendRight})");
            Assert.True(whiteMinX > friendMaxX, $"the name starts right of the cell (text from x {whiteMinX}, the cell to x {friendMaxX})");
            Assert.True(hostileNear > 150, $"the tank platoon's hostile diamond stands at its point ({hostileNear} px)");
        }
        finally { Raylib.UnloadImage(image); }
    }
}
