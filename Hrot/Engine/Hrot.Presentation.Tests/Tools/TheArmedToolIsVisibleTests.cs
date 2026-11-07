using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.ScenarioEditor.Map;
using Hrot.ScenarioEditor.Tools;
using Hrot.UI.Common.Panels;
using Xunit;

namespace Hrot.Presentation.Tests.Tools;

/// <summary>
/// ⭐ B-17 P1 (📄 docs/UX/DESIGN_Map_Interaction.md §2.4, §2.5 <i>"the armed tool is visible"</i>; mockup <c>map-interaction-visual.svg</c>) — every
/// map tool says what its next click does (<see cref="ToolDescriptor.Hint"/>); the controller's <see cref="ToolController.ActiveHint"/> is the ONE
/// answer (a gizmo may state its own line); the status line and the cursor hint both read it; both are composed by the shared pack / cursor
/// section, so every host has them.
/// </summary>
[Collection(Hrot.Presentation.Tests.Map3D.Map3DFrameRail.Serial)]
public sealed class TheArmedToolIsVisibleTests : IDisposable
{
    private readonly EntityRepository _world = new();

    public TheArmedToolIsVisibleTests() => _world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();

    public void Dispose() => _world.Dispose();

    private sealed class NoInput : IInputProvider
    {
        public Vector2 MousePosition { get; set; }
        public Vector2 MouseDelta => Vector2.Zero;
        public float MouseWheelMove => 0f;
        public bool IsMouseButtonPressed(MapMouseButton b) => false;
        public bool IsMouseButtonDown(MapMouseButton b) => false;
        public bool IsMouseButtonReleased(MapMouseButton b) => false;
        public bool IsKeyPressed(MapKeyboardKey k) => false;
        public bool IsKeyDown(MapKeyboardKey k) => false;
        public bool IsKeyReleased(MapKeyboardKey k) => false;
        public int GetKeyPressed() => 0;
        public bool IsMouseCaptured => false;
        public bool IsKeyboardCaptured => false;
    }

    /// <summary>
    /// ⭐ Every tool the shared registrations arm on a map (bar the plain Select) carries a hint and an icon; the active hint is the descriptor's,
    /// a gizmo's own line overrides it, and arming another tool clears the override and the cursor claim. 🔴 Red-proof: the override not cleared
    /// on a tool change ⇒ red.
    /// </summary>
    [Fact]
    public void P1_EveryMapToolSaysWhatTheNextClickDoes_AndTheActiveHintIsOneAnswer()
    {
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _world });
        foreach (var id in new[] { ScenarioToolIds.Spawn, ScenarioToolIds.Edit, ScenarioToolIds.Route, ScenarioToolIds.Measure, ScenarioToolIds.Rotate,
                                   ScenarioToolIds.Plan })
        {
            var d = mi.Tools.DescriptorOf(id)!;
            Assert.False(string.IsNullOrEmpty(d.Hint), $"{id} has a hint");
            Assert.False(string.IsNullOrEmpty(d.Glyph), $"{id} has an icon");
        }

        Assert.Null(mi.Tools.ActiveHint);
        Assert.True(mi.Tools.Activate(ScenarioToolIds.Measure));
        Assert.Equal(mi.Tools.ActiveModal!.Hint, mi.Tools.ActiveHint);
        mi.Tools.SetHint("1 point · click: end");
        mi.Tools.ClaimCursorHint();
        Assert.Equal("1 point · click: end", mi.Tools.ActiveHint);
        Assert.True(mi.Tools.CursorHintClaimed);

        Assert.True(mi.Tools.Activate(ScenarioToolIds.Select));
        Assert.True(mi.Tools.Activate(ScenarioToolIds.Measure));
        Assert.Equal(mi.Tools.ActiveModal!.Hint, mi.Tools.ActiveHint);   // the override went with the tool it was said for
        Assert.False(mi.Tools.CursorHintClaimed);
    }

    /// <summary>
    /// ⭐ The cursor hint is GIZMO PRIMITIVES (so a remote terminal draws it): the armed tool's hint at the cursor, a line below it; nothing for
    /// Select, nothing when the tool's gizmo claimed the cursor; the pack makes the controller findable from the world. 🔴 Red-proof: the pack not
    /// putting its controller on the world (<c>MapUiStates.Set</c>) ⇒ no text ⇒ red.
    /// </summary>
    [Fact]
    public void P1_TheCursorHint_IsDrawnAtTheCursor_AsGizmoText()
    {
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _world });
        Assert.Same(mi.Tools, MapUiStates.ToolsOf(_world));
        var canvas = new MapCanvas(new NoInput()) { Camera = new MapCamera { Zoom = 1f, Target = Vector2.Zero, Offset = Vector2.Zero } };
        var readout = new MapCursorReadout();
        canvas.AddResource(readout);
        MapUiStates.Set(_world, canvas);
        readout.Set(new MapCursorSample(new Vector3(120, -40, 0), false, 0, 0, 0));

        Text[] Drawn()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 16);
            new ToolHintGizmo().Draw(_world, buffer);
            return buffer.GetFrame().ToArray().Where(p => p.Shape == DebugPrimitiveShape.Text)
                .Select(p => new Text(p.TextX, p.TextY, p.StringHash != 0 ? buffer.InternMap.TryResolve(p.StringHash) : p.TextContent.ToString(), p.LineOffsetPx)).ToArray();
        }

        Assert.Empty(Drawn());   // nothing armed
        mi.Tools.Activate(ScenarioToolIds.Measure);
        var hint = Assert.Single(Drawn());
        Assert.Equal(new Text(120, -40, mi.Tools.ActiveHint, ToolHintGizmo.BelowCursorPx), hint);
        mi.Tools.ClaimCursorHint();
        Assert.Empty(Drawn());   // the gizmo draws its own line there
        mi.Tools.Activate(ScenarioToolIds.Select);
        Assert.Empty(Drawn());   // plain Select says nothing
    }

    /// <summary>
    /// ⭐ A gizmo text longer than 31 characters (<c>DrawTextLong</c>) is drawn WHOLE: the ONE text reader resolves its interned string through the
    /// buffer the layer is drawing. 🔴 Found by P1's fidelity frame: every map cut such a line (a tool hint, the placement hint) at 31 characters.
    /// </summary>
    [Fact]
    public void P1_ALongGizmoText_IsReadWhole()
    {
        const string hint = "click: start · click: end · right-click or Esc: cancel";
        var buffer = new DebugPrimitiveBuffer(capacity: 4);
        buffer.DrawTextLong(1, 2, hint, new Rgba32(255, 255, 255));
        var p = buffer.GetFrame()[0];
        var saved = GizmoMap.Presentation.GizmoTextDraw.Strings;
        try
        {
            GizmoMap.Presentation.GizmoTextDraw.Strings = buffer.InternMap;
            Assert.Equal(hint, GizmoMap.Presentation.GizmoTextDraw.TextOf(in p));
            GizmoMap.Presentation.GizmoTextDraw.Strings = null;
            var preview = GizmoMap.Presentation.GizmoTextDraw.TextOf(in p);   // without the map: the inline preview only (31 BYTES of UTF-8)
            Assert.True(preview.Length < hint.Length && hint.StartsWith(preview, StringComparison.Ordinal), preview);
        }
        finally { GizmoMap.Presentation.GizmoTextDraw.Strings = saved; }
    }

    private readonly record struct Text(float X, float Y, string? Value, float OffsetPx);

    /// <summary>
    /// ⭐ The status line: registered BY the cursor section (so every host that shows the cursor reading shows the armed tool), it shows the armed
    /// tool through the canvas's link to its controller — and nothing for Select. 🔴 Red-proof: the cursor section not registering it ⇒ red.
    /// </summary>
    [Fact]
    public void P1_TheStatusLine_ShowsTheArmedTool_RegisteredWithTheCursorSection()
    {
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _world });
        var canvas = new MapCanvas(new NoInput()) { Camera = new MapCamera { Zoom = 1f, Target = Vector2.Zero, Offset = Vector2.Zero } };
        canvas.AddResource(new MapToolsLink(() => MapUiStates.ToolsOf(_world)));
        var bar = new Fdp.Presentation.WindowManager.StatusBarManager();
        MapCursorStatusBarSection.Register(bar, () => canvas, "Scenario");
        Assert.Contains(ToolStatusSection.SectionId, bar.SectionIds);

        var section = new ToolStatusSection(() => canvas);
        Assert.Null(section.Shown());
        mi.Tools.Activate(ScenarioToolIds.Measure);
        Assert.Equal(ScenarioToolIds.Measure, section.Shown()?.Id);
        mi.Tools.Activate(ScenarioToolIds.Select);
        Assert.Null(section.Shown());
    }

    /// <summary>
    /// ⭐ Fidelity (R-4146) — Measure armed on a real frame: the cursor hint under the cursor on the map, and the status line (the teal icon, the
    /// tool's name, the grey hint) as the mockup's line under the map. Renders <c>armed-tool.png</c> under <c>HROT_RAIL_SHOTS</c>; asserts the
    /// teal icon and the hint text are drawn.
    /// </summary>
    [SkippableFact]
    public void P1_TheArmedTool_OnARealFrame()
    {
        Skip.IfNot(Hrot.Editor.UiFrameRail.UiFrameHarness.IsAvailable(), Hrot.Editor.UiFrameRail.UiFrameHarness.UnavailableReason);
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _world });
        using var frame = Hrot.Editor.UiFrameRail.UiFrameHarness.Begin(800, 420);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        var canvas = new MapCanvas(input: null);
        var camera = new MapCamera();
        const float zoom = 2f;
        var target = new Vector2(-800f / 2f / zoom, 380f / 2f / zoom);
        camera.ApplyCameraView(new MapCameraView { Target = target, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom });
        canvas.Camera = camera;
        var readout = new MapCursorReadout();
        canvas.AddResource(readout);
        canvas.AddResource(new MapToolsLink(() => MapUiStates.ToolsOf(_world)));
        MapUiStates.Set(_world, canvas);
        readout.Set(new MapCursorSample(new Vector3(-60, 20, 0), false, 0, 0, 0));
        var buffer = new DebugPrimitiveBuffer();
        var gizmos = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus(), camera: camera);
        canvas.AddLayer(gizmos);
        mi.Tools.Activate(ScenarioToolIds.Measure);
        new ToolHintGizmo().Draw(_world, buffer);
        buffer.DrawSphere(new Vector3(-60, 20, 0), 6f, new Rgba32(255, 255, 255), thickness: 1.5f, sizeMode: SizeMode.ScreenPixels);   // the cursor, for the picture
        gizmos.Update(0.016f);
        var section = new ToolStatusSection(() => canvas);
        void Draw()
        {
            canvas.Draw();
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(0, 390)); ImGuiNET.ImGui.SetNextWindowSize(new Vector2(800, 30));
            ImGuiNET.ImGui.Begin("status", ImGuiNET.ImGuiWindowFlags.NoDecoration);
            section.Render();
            ImGuiNET.ImGui.End();
        }
        frame.StepN(3, Draw);
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-armed-tool");
        System.IO.Directory.CreateDirectory(dir);
        string png = System.IO.Path.Combine(dir, "armed-tool.png");
        frame.Screenshot(png);
        var image = Raylib_cs.Raylib.LoadImage(png);
        int teal = 0, hintPx = 0;
        try
        {
            var g = ToolStatusSection.GlyphColour;
            int gr = (int)(g.X * 255), gg = (int)(g.Y * 255), gb = (int)(g.Z * 255);
            for (int y = 390; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    var c = Raylib_cs.Raylib.GetImageColor(image, x, y);
                    if (Math.Abs(c.R - gr) + Math.Abs(c.G - gg) + Math.Abs(c.B - gb) < 60) teal++;
                }
            for (int y = 0; y < 380; y++)   // the map: light hint text near the cursor (screen x of world -60 = (-60 - target.X) * zoom)
                for (int x = 0; x < image.Width; x++)
                {
                    var c = Raylib_cs.Raylib.GetImageColor(image, x, y);
                    if (c.R > 200 && c.G > 200 && c.B > 200) hintPx++;
                }
        }
        finally { Raylib_cs.Raylib.UnloadImage(image); }
        Assert.True(teal > 10, $"the status line's tool icon is drawn teal ({teal} px)");
        Assert.True(hintPx > 60, $"the cursor hint is drawn on the map ({hintPx} light px)");
    }
}
