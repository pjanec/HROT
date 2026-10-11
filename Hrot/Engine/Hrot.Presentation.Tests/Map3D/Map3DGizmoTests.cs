using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-1033 S3 — gizmos in 3-D (<c>docs/DESIGN_Map_3D_Mode.md</c> §6e): the shared triage resolves anchored shapes in 3-D and
/// honours the map's filters; the drawer drapes z = 0 shapes over relief (M18, R-248), draws shapes with height as given,
/// counts what it cannot draw, and hands labels to the overlay, which lifts them onto the entity's body (M13).
/// </summary>
public sealed class Map3DGizmoTests
{
    private static readonly Rgba32 Red = new(255, 0, 0);
    private static readonly Rgba32 Blue = new(0, 0, 255);

    private sealed class CaptureSink : IGizmoSink3D
    {
        public readonly List<(Vector3 A, Vector3 B, Rgba32 Ca, Rgba32 Cb)> Lines = new();
        public readonly List<(Vector3 A, Vector3 B, Vector3 C)> Triangles = new();
        public readonly List<(Vector3 Centre, float Radius)> Spheres = new();
        public void Line(Vector3 a, Vector3 b, Rgba32 ca, Rgba32 cb) => Lines.Add((a, b, ca, cb));
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Rgba32 colour) => Triangles.Add((a, b, c));
        public void Sphere(Vector3 centre, float radius, Rgba32 colour, Rgba32 fill) => Spheres.Add((centre, radius));
    }

    private static List<TriagedPrimitive3D> Triage(DebugPrimitiveTriage3D triage, params DebugPrimitive[] prims)
    {
        var into = new List<TriagedPrimitive3D>();
        triage.Triage(prims, into);
        return into;
    }

    /// <summary>A slope: the ground rises half a metre per metre east (R-248 — never flat).</summary>
    private static float Slope(float x, float y) => 0.5f * x;

    [Fact]
    public void S3_Triage_ResolvesAnAnchoredLine_In3D_TurnedByHeading_AndLiftedByTheAnchorHeight()
    {
        var anchor = DebugPrimitive.MakeSpatialAnchor(77, 100f, 50f, 12f, headingDeg: 90f);
        var line = DebugPrimitive.MakeLine(Vector3.Zero, new Vector3(10f, 0f, 2f), Red);
        line.Space = CoordinateSpace.EntityLocal;
        line.AnchorIndex = 77;

        var t = Assert.Single(Triage(new DebugPrimitiveTriage3D(), anchor, line));
        Assert.True(t.Anchored);
        Assert.Equal(77, t.AnchorId);
        Assert.Equal(CoordinateSpace.World, t.Primitive.Space);
        AssertNear(new Vector3(100f, 50f, 12f), t.Primitive.LineStart);
        AssertNear(new Vector3(100f, 60f, 14f), t.Primitive.LineEnd);   // heading 90°: local +X points north
    }

    [Fact]
    public void S3_Triage_TheMapsFilters_TargetView_LayerMask_Lod_AndAMissingAnchor_EachCounted()
    {
        var triage = new DebugPrimitiveTriage3D
        {
            AcceptedTargets = PipelineTarget.Map2D | PipelineTarget.Viewport3D,
            HonourLayerMask = true,
            ZoomAt = _ => 2f,   // 2 px per metre everywhere
        };
        var mask = new LayerMask256();
        mask.SetBit(0);
        var kept = DebugPrimitive.MakeLine(Vector3.Zero, Vector3.UnitX, Red, target: PipelineTarget.Map2D);
        var graphOnly = DebugPrimitive.MakeLine(Vector3.Zero, Vector3.UnitX, Red, target: PipelineTarget.NodeGraph);
        var hiddenLayer = DebugPrimitive.MakeLine(Vector3.Zero, Vector3.UnitX, Red, target: PipelineTarget.Map2D, layer: 5);
        var closeOnly = DebugPrimitive.MakeLine(Vector3.Zero, Vector3.UnitX, Red, target: PipelineTarget.Map2D);
        closeOnly.MinZoomLod = 40;   // needs 10 px/m
        var dangling = DebugPrimitive.MakeLine(Vector3.Zero, Vector3.UnitX, Red, target: PipelineTarget.Map2D);
        dangling.Space = CoordinateSpace.EntityLocal;
        dangling.AnchorIndex = 999;

        var into = Triage(triage, DebugPrimitive.MakeLayerControlMask(mask), kept, graphOnly, hiddenLayer, closeOnly, dangling);
        Assert.Single(into);
        Assert.Equal((1, 1, 1, 1), (triage.CulledByTarget, triage.CulledByLayer, triage.CulledByLod, triage.DanglingAnchors));
    }

    [Fact]
    public void S3_AGroundLine_IsDrapedOverAHill_Subdivided_NotCutThroughIt()
    {
        var r = new GizmoRenderer3D { GroundHeight = Slope };
        var sink = new CaptureSink();
        var line = DebugPrimitive.MakeLine(Vector3.Zero, new Vector3(30f, 0f, 0f), Red);
        r.Draw(Triage(new DebugPrimitiveTriage3D(), line), _ => 10f, sink);

        Assert.True(sink.Lines.Count >= 10, $"30 m at a {GizmoRenderer3D.DrapeStep} m step — got {sink.Lines.Count} pieces");
        foreach (var (a, b, _, _) in sink.Lines)
        {
            Assert.InRange(a.Z - Slope(a.X, a.Y), 0.05f, 0.2f);   // on the ground there, a little above it
            Assert.InRange(b.Z - Slope(b.X, b.Y), 0.05f, 0.2f);
        }
    }

    [Fact]
    public void S3_AShapeWithHeight_IsDrawnAsGiven_AFireTraceStaysAtTheMuzzle()
    {
        var r = new GizmoRenderer3D { GroundHeight = Slope };
        var sink = new CaptureSink();
        var trace = DebugPrimitive.MakeLine(new Vector3(0f, 0f, 2.4f), new Vector3(50f, 0f, 1.2f), Red);
        r.Draw(Triage(new DebugPrimitiveTriage3D(), trace), _ => 10f, sink);

        var seg = Assert.Single(sink.Lines);
        AssertNear(new Vector3(0f, 0f, 2.4f), seg.A);
        AssertNear(new Vector3(50f, 0f, 1.2f), seg.B);
    }

    [Fact]
    public void S3_Spheres_ABurstWithHeightIsASphere_AGroundRingIsDraped_AFilledRingIsAFan()
    {
        var r = new GizmoRenderer3D { GroundHeight = Slope };
        var sink = new CaptureSink();
        var burst = DebugPrimitive.MakeSphere(new Vector3(10f, 0f, 7f), 20f, Red, thickness: 1.5f);
        var ring = DebugPrimitive.MakeSphere(new Vector3(40f, 0f, 0f), 3f, Red, thickness: 1f);
        var disc = DebugPrimitive.MakeSphere(new Vector3(80f, 0f, 0f), 2f, Red, fillColor: Blue);
        r.Draw(Triage(new DebugPrimitiveTriage3D(), burst, ring, disc), _ => 10f, sink);

        var s = Assert.Single(sink.Spheres);
        Assert.Equal(20f, s.Radius);
        Assert.Contains(sink.Lines, l => MathF.Abs(l.A.X - 43f) < 0.2f && MathF.Abs(l.A.Z - Slope(43f, 0f)) < 0.2f);
        Assert.True(sink.Triangles.Count >= 20, "a filled ring is a fan of draped triangles");
        Assert.All(sink.Triangles, t => Assert.InRange(t.A.Z - Slope(t.A.X, t.A.Y), 0.05f, 0.2f));
    }

    [Fact]
    public void S3_ScreenPixelSizes_BecomeMetresAtTheShapesDistance()
    {
        var r = new GizmoRenderer3D();
        var sink = new CaptureSink();
        // a 6 px handle where the camera shows 2 px per metre ⇒ 3 m
        var handle = DebugPrimitive.MakeSphere(new Vector3(0f, 0f, 5f), 6f, Red, sizeMode: SizeMode.ScreenPixels);
        r.Draw(Triage(new DebugPrimitiveTriage3D(), handle), _ => 2f, sink);
        Assert.Equal(3f, Assert.Single(sink.Spheres).Radius, 3);
    }

    [Fact]
    public void S3_WhatCannotBeDrawn_IsCounted_LabelsAndPanelsAreHandedOn()
    {
        var r = new GizmoRenderer3D();
        var sink = new CaptureSink();
        var anchor = DebugPrimitive.MakeSpatialAnchor(5, 0f, 0f, 0f, 0f);
        var body = DebugPrimitive.MakeSemanticShape(5, 5, 1ul, 6f, 3f, 0u);
        var sym = default(DebugPrimitive);
        sym.Shape = DebugPrimitiveShape.MilStd2525;
        sym.TargetView = PipelineTarget.All;
        var text = DebugPrimitive.MakeText(1f, 2f, new FixedString32("T-72"), Red);
        var panel = DebugPrimitive.MakeStructInspector(5, 0xABCu);
        r.Draw(Triage(new DebugPrimitiveTriage3D(), anchor, body, sym, text, panel), _ => 1f, sink);

        Assert.Equal(1, r.Skipped[DebugPrimitiveShape.SemanticShape]);
        Assert.Equal(1, r.Skipped[DebugPrimitiveShape.MilStd2525]);
        Assert.Equal(2, r.Skipped.Total);
        Assert.Single(r.Labels);
        Assert.Single(r.Panels);
    }

    [Fact]
    public void S3_Labels_AnAnchoredNameSitsOnTopOfTheBody_AGroundLabelOnTheGround_BehindTheCameraIsNotDrawn()
    {
        var r = new GizmoRenderer3D { GroundHeight = Slope };
        var anchor = DebugPrimitive.MakeSpatialAnchor(42, 20f, 0f, 10f, 0f);
        var name = DebugPrimitive.MakeText(0f, 0f, new FixedString32("Leopard"), Red);
        name.Space = CoordinateSpace.EntityLocal;
        name.AnchorIndex = 42;
        var measure = DebugPrimitive.MakeText(30f, 0f, new FixedString32("120 m"), Red);
        r.Draw(Triage(new DebugPrimitiveTriage3D(), anchor, name, measure), _ => 1f, new CaptureSink());

        Assert.Equal(2, r.Labels.Count);
        var onTank = r.Labels.Single(l => l.AnchorId == 42);
        AssertNear(new Vector3(20f, 0f, 10f), onTank.World);
        var onGround = r.Labels.Single(l => l.AnchorId == 0);
        Assert.Equal(Slope(30f, 0f), onGround.World.Z, 3);

        // the camera looks north at the tank from 60 m south, level with the body top
        var cam = new MapCamera3D { ViewportOverride = new Vector2(1280, 720), LookAt = new Vector3(20f, 0f, 12.5f), Distance = 60f, Pitch = 0f, Yaw = MathF.PI / 2f };
        var lifted = LabelOverlay3D.ScreenOf(in onTank, cam, id => id == 42 ? 2.5f : null);
        Assert.NotNull(lifted);
        Assert.InRange(lifted!.Value.Y, 355f, 365f);   // the body top (10 + 2.5 m) is the screen centre's height
        var unlifted = LabelOverlay3D.ScreenOf(in onTank, cam, _ => null);
        Assert.True(unlifted!.Value.Y > lifted.Value.Y, "an unknown body still lifts the label (default lift)");

        var behind = new GizmoLabel3D(name, new Vector3(20f, -200f, 10f), 0);
        Assert.Null(LabelOverlay3D.ScreenOf(in behind, cam, null));
    }

    [Fact]
    public void S3_DashedLines_KeepPixelDashes_AndGradientsBlendAlongTheLine()
    {
        var r = new GizmoRenderer3D();
        var sink = new CaptureSink();
        var dashed = DebugPrimitive.MakeLine(new Vector3(0f, 0f, 5f), new Vector3(16f, 0f, 5f), Red, style: LineStyle.Dashed);
        r.Draw(Triage(new DebugPrimitiveTriage3D(), dashed), _ => 1f, sink);   // 8 px on / 8 px off at 1 px/m ⇒ one 8 m dash
        var dash = Assert.Single(sink.Lines);
        Assert.Equal(8f, Vector3.Distance(dash.A, dash.B), 2);

        sink.Lines.Clear();
        var grad = DebugPrimitive.MakeLine(Vector3.Zero, new Vector3(9f, 0f, 0f), Red);
        grad.EndColor = Blue;
        r.Draw(Triage(new DebugPrimitiveTriage3D(), grad), _ => 1f, sink);
        Assert.Equal(Red, sink.Lines[0].Ca);
        Assert.Equal(Blue, sink.Lines[^1].Cb);
    }

    [Fact]
    public void S4_AMetreWideLine_IsADrapedRibbonAtItsTrueWidth_ARoad()
    {
        var r = new GizmoRenderer3D { GroundHeight = Slope };
        var sink = new CaptureSink();
        var road = DebugPrimitive.MakeLine(new Vector3(0f, 10f, 0f), new Vector3(30f, 10f, 0f), Red, thickness: 7f, sizeMode: SizeMode.WorldMeters);
        r.Draw(Triage(new DebugPrimitiveTriage3D(), road), _ => 10f, sink);

        Assert.Empty(sink.Lines);
        Assert.True(sink.Triangles.Count >= 20, $"30 m in 3 m pieces, two triangles each — got {sink.Triangles.Count}");
        float minY = sink.Triangles.Min(t => MathF.Min(t.A.Y, MathF.Min(t.B.Y, t.C.Y)));
        float maxY = sink.Triangles.Max(t => MathF.Max(t.A.Y, MathF.Max(t.B.Y, t.C.Y)));
        Assert.Equal(7f, maxY - minY, 2);                                                    // the road's width
        Assert.All(sink.Triangles, t => Assert.InRange(t.A.Z - Slope(t.A.X, t.A.Y), 0.05f, 0.2f));   // on the slope
        // a pixel-wide line stays a line
        sink.Triangles.Clear();
        r.Draw(Triage(new DebugPrimitiveTriage3D(), DebugPrimitive.MakeLine(Vector3.Zero, new Vector3(9f, 0f, 0f), Red, thickness: 7f)), _ => 10f, sink);
        Assert.Empty(sink.Triangles);
    }

    [Fact]
    public void S3_TheGizmoLayer_DrawsIn3D_WithTheMapsFilters_AndAttachMapLayersWiresGroundAndLabels()
    {
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        var layer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new Fdp.Core.FdpEventBus());
        Assert.True(((Fdp.Toolkit.Vis2D.Abstractions.IMapLayer)layer).Has3D);
        layer.Renderer3D.GroundHeight = Slope;
        var sink = new CaptureSink();
        var area = DebugPrimitive.MakeLine(Vector3.Zero, new Vector3(9f, 0f, 0f), Red, target: PipelineTarget.Map2D);
        var nodeGraph = DebugPrimitive.MakeLine(Vector3.Zero, new Vector3(9f, 0f, 0f), Red, target: PipelineTarget.NodeGraph);
        layer.Draw3D(new[] { area, nodeGraph }, _ => 10f, sink);
        Assert.Equal(1, layer.Triage3D.CulledByTarget);
        Assert.NotEmpty(sink.Lines);

        // the pack sets both seams on every host (the source rail EveryMapHostAttachesTheSharedLayers pins every host calls it)
        string pack = File.ReadAllText(Path.Combine(RepoRoot(), "Hrot/Engine/Hrot.Presentation/ScenarioEditor/Map/MapInteractionPack.cs"));
        Assert.Contains("gizmoLayer.Renderer3D.GroundHeight = viewSwitch.Camera3D.GroundHeight;", pack);
        Assert.Contains("gizmoLayer.LabelLift = bodies.TopAbove;", pack);
    }

    // ── S5b — the entity CARD (§3.6, M14): one canvas per entity, the same in 2-D and 3-D ──

    private static DebugPrimitive[] CardFrame(long net, Action<IDebugDrawBuilder> draw)
    {
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        draw(buffer);
        return buffer.GetFrame().ToArray();
    }

    /// <summary>A text measure without a font: 7 px per character at any size.</summary>
    private static float Measure(string s, float px) => 7f * s.Length;

    [Fact]
    public void S5b_TheCardSugar_EmitsOrdinaryPrimitives_KeyedByTheNetworkId_RowInZIndex()
    {
        var prims = CardFrame(1043, d =>
        {
            var card = d.Card(1043, DebugTraceLayers.Labels);
            card.Row(0).Frame(Red);
            card.Row(10).Bar(0.25f, Blue, Red);
            card.Row(20).Text("T-72 #1043", Blue);
        });

        Assert.Equal(5, prims.Length);   // frame: back + outline · bar: back + fill · text
        Assert.All(prims, p =>
        {
            Assert.Equal(CoordinateSpace.EntityCard, p.Space);
            Assert.Equal(1043, p.AnchorIndex);
            Assert.Equal(DebugTraceLayers.Labels, p.DebugLayer);
        });
        Assert.Equal(new byte[] { 0, 0, 10, 10, 20 }, prims.Select(p => p.ZIndex).ToArray());
        Assert.Equal(0.25f, 2f * prims[3].BoxExtentX, 3);
        Assert.Equal(SizeMode.ScreenPercent, prims[3].SizeMode);
    }

    [Fact]
    public void S5b_Layout_StacksRowsByOrder_SizesToTheWidestRow_SitsAboveTheEntity_LeaderInTheFrameColour()
    {
        var prims = CardFrame(7, d =>
        {
            var card = d.Card(7);
            card.Row(20).Text("a name that is long", Blue);   // emitted first, still the LOWER row
            card.Row(10).Bar(1f, Blue, Red, heightPx: 4f);
            card.Row(0).Frame(Red);
        }).Concat(CardFrame(8, d => d.Card(8).Row(20).Text("off screen", Blue))).ToArray();
        var at = new Vector2(400f, 300f);

        var cards = new GizmoMap.Presentation.EntityCardRenderer().Layout(prims, id => id == 7 ? at : null, Measure);

        var c = Assert.Single(cards);   // entity 8 has no screen point ⇒ no card
        Assert.Equal(7, c.NetworkId);
        float textW = Measure("a name that is long", 13f);
        Assert.Equal(textW + 2f * GizmoMap.Presentation.EntityCardRenderer.Padding, c.Rect.Width, 3);
        Assert.Equal(at.X - c.Rect.Width / 2f, c.Rect.X, 3);                                            // centred over the entity
        Assert.Equal(at.Y - GizmoMap.Presentation.EntityCardRenderer.Leader, c.Rect.Y + c.Rect.Height, 3);   // a leader's length above it
        var bar = c.Items.First(i => i.Primitive.ZIndex == 10);
        var name = c.Items.First(i => i.Primitive.ZIndex == 20);
        Assert.True(bar.RowOrigin.Y < name.RowOrigin.Y, "row 10 is above row 20 whatever the emit order");
        Assert.Equal(bar.RowOrigin.Y + 4f + GizmoMap.Presentation.EntityCardRenderer.RowGap, name.RowOrigin.Y, 3);
        Assert.All(c.Items.Where(i => i.Primitive.ZIndex == 0), i => Assert.True(i.CardWide));
        Assert.Equal((byte)255, c.LeaderColour.R);   // the outline's colour (red), not the default grey
        Assert.Equal((byte)0, c.LeaderColour.G);
    }

    [Fact]
    public void S5b_Layout_AWorldShapeInACard_IsCounted_NotDrawn_AndAnEmptyCardIsAtLeastTheMinimumWidth()
    {
        var sphere = DebugPrimitive.MakeSphere(Vector3.Zero, 1f, Red);
        sphere.Space = CoordinateSpace.EntityCard;
        sphere.AnchorIndex = 3;
        var r = new GizmoMap.Presentation.EntityCardRenderer();
        var frameOnly = CardFrame(4, d => d.Card(4).Row(0).Frame(Blue));

        var cards = r.Layout(frameOnly.Append(sphere), _ => Vector2.Zero, Measure);

        Assert.Equal(1, r.Skipped[DebugPrimitiveShape.Sphere]);
        var c = Assert.Single(cards);
        Assert.Equal(4, c.NetworkId);
        Assert.Equal(GizmoMap.Presentation.EntityCardRenderer.MinWidth, c.Rect.Width, 3);
    }

    [Fact]
    public void S5b_In3D_CardPrimitives_AreHandedToTheCardPass_NotDrawnAsShapes_AndTheTriageAnswersTheEntitysAnchor()
    {
        var triage = new DebugPrimitiveTriage3D();
        var anchor = DebugPrimitive.MakeSpatialAnchor(1043, 100f, 50f, 12f, 0f);
        var card = CardFrame(1043, d => d.Card(1043).Row(10).Bar(0.5f, Blue, Red));
        var r = new GizmoRenderer3D();
        var sink = new CaptureSink();

        r.Draw(Triage(triage, card.Prepend(anchor).ToArray()), _ => 1f, sink);

        Assert.Equal(2, r.Cards.Count);
        Assert.Empty(sink.Triangles);
        Assert.Empty(sink.Lines);
        Assert.True(triage.TryGetAnchor(1043, out var at));
        AssertNear(new Vector3(100f, 50f, 12f), at);
        Assert.False(triage.TryGetAnchor(99, out _));
    }

    [Fact]
    public void S5b_In2D_TheCardSitsOverTheEntitysSymbol_ThroughTheNorthUpCamera()
    {
        var cam = new Fdp.Toolkit.Vis2D.Components.MapCamera();
        var target = new Vector2(0f, 100f);
        cam.ApplyCameraView(new Fdp.Toolkit.Vis2D.Components.MapCameraView { Target = target, Zoom = 2f, SmoothTarget = target, SmoothZoom = 2f });
        var frame = new[] { DebugPrimitive.MakeSpatialAnchor(5, 30f, 40f, 0f, 0f) };

        var anchorOf = Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer.CardAnchor2D(cam, frame);

        Assert.Null(anchorOf(6));
        var s = anchorOf(5)!.Value;
        var symbol = cam.WorldToScreen(new Vector2(30f, 40f));
        Assert.Equal(symbol.X, s.X, 3);
        Assert.Equal(symbol.Y - 6f, s.Y, 3);   // just above the symbol's centre
        Assert.True(cam.WorldToScreen(new Vector2(30f, 50f)).Y < symbol.Y, "north-up: a point further north is HIGHER on screen");
    }

    [Fact]
    public void S5b_TheCardGizmos_FrameInTheSideColour_NameWithTheId_HitPointsAndBehaviourRows()
    {
        using var w = new Fdp.Core.EntityRepository();
        w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        w.RegisterComponent<Fdp.Core.EntityInfo>();
        var e = w.CreateEntity();
        w.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity(1043));
        w.AddComponent(e, new Fdp.Core.EntityInfo { Name = new Fdp.Core.FixedString64("T-72"), ForceId = Fdp.Core.ForceId.Hostile });
        var unnamed = w.CreateEntity();
        w.AddComponent(unnamed, new Fdp.Toolkit.Replication.Components.NetworkIdentity(9));
        w.AddComponent(unnamed, new Fdp.Core.EntityInfo());
        var d = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();

        new Hrot.Common.Diagnostics.Gizmos.EntityCardFrameGizmo().Draw(w, e, d);
        new Hrot.Common.Diagnostics.Gizmos.EntityNameGizmo().Draw(w, e, d);
        new Hrot.Common.Diagnostics.Gizmos.EntityNameGizmo().Draw(w, unnamed, d);
        var prims = d.GetFrame().ToArray();

        var outline = prims.Single(p => p.ZIndex == 0 && p.ThicknessU16 > 0);
        Assert.Equal(Hrot.Common.Diagnostics.Gizmos.SidePalette.Hostile, outline.Color);
        var names = prims.Where(p => p.Shape == DebugPrimitiveShape.Text).Select(p => p.TextContent.ToString()).ToArray();
        Assert.Equal(new[] { "T-72 #1043", "#9" }, names);
        Assert.All(prims, p => Assert.Equal(DebugTraceLayers.Labels, p.DebugLayer));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "HROT.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, float tol = 1e-3f)
        => Assert.True(Vector3.Distance(expected, actual) < tol, $"expected {expected}, got {actual}");
}
