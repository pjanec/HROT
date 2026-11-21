using System.Numerics;
using System.Reflection;
using Fdp.Presentation.Fonts;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Visuals;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using NodeEditor.Core;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Widgets;
using NodeEditor.Primitives;
using Raylib_cs;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor.Frame;

/// <summary>
/// ⭐ U-3 (docs/UX/DESIGN_Colour_Icons.md §3 / §4 I3) — the Blueprint canvas's attachment pills: each pill's GLYPH is drawn in its <c>bp/*</c> colour (the icon map's) while its LABEL stays the pill's text colour. The real
/// production painter (<c>AttachmentRenderer.DrawPillText</c>, internal to NodeEditor.UI, reached by reflection) paints the four real attachments in a REAL frame (real fonts, Xvfb); the screen is read.
/// 🔴 Red-proof (compiles): <c>DrawPillText</c> painting the glyph in <c>textColor</c> ⇒ no pixel in the glyph's colour ⇒ red.
/// Render (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>colour-icons-pills.png</c>.
/// </summary>
[Collection(UiFrameCollection.Name)]
public sealed class TheAttachmentPillsDrawTheirGlyphInColourTests
{
    private static bool Near(Color c, Vector4 v, int tol) =>
        Math.Abs(c.R - v.X * 255f) <= tol && Math.Abs(c.G - v.Y * 255f) <= tol && Math.Abs(c.B - v.Z * 255f) <= tol;

    private static int Count(Image shot, int x0, int y0, int x1, int y1, Func<Color, bool> what)
    {
        int n = 0;
        for (int x = x0; x < x1; x++) for (int y = y0; y < y1; y++) if (what(Raylib.GetImageColor(shot, x, y))) n++;
        return n;
    }

    [Fact]
    public void EveryAttachmentNamesItsBpIcon()
    {
        var node = new SpawnEqsSensorNode { Id = Guid.NewGuid() };
        void Is(IAttachmentModel a, string key) { Assert.Equal(Icons.Get(key).Glyph, a.Glyph); Assert.Equal(Icons.Get(key).Color, a.GlyphColor); }
        Is(new EqsTemplateAttachment(node, new EqsTemplateRegistry()), "bp/eqs-template");
        Is(new CrossAssetDependencyAttachment(new NodeId(Guid.NewGuid()), "Other"), "bp/cross-asset");
        Is(new ConditionSummaryAttachment(new WhenNode { Id = Guid.NewGuid() }), "bp/condition");
        Is(new ReadEqsResultAttachment(new ReadEqsResultNode { Id = Guid.NewGuid(), SensorVariableName = "v" }), "bp/eqs-result");
    }

    [SkippableFact]
    public void ThePillsGlyphIsInItsColour_TheLabelStaysTheTextColour()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(700, 200);
        new EditorFontService().Initialize(1f, 1f);
        var draw = Assembly.Load("NodeEditor.UI").GetType("NodeEditor.UI.Canvas.AttachmentRenderer")!.GetMethod("DrawPillText", BindingFlags.NonPublic | BindingFlags.Static)!;
        var theme = new DefaultTheme();
        var pills = new IAttachmentModel[]
        {
            new EqsTemplateAttachment(new SpawnEqsSensorNode { Id = Guid.NewGuid() }, new EqsTemplateRegistry()),
            new CrossAssetDependencyAttachment(new NodeId(Guid.NewGuid()), "Other asset"),
            new ConditionSummaryAttachment(new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.ConditionMet, Edges = WhenEdge.RisingEdge }),
            new ReadEqsResultAttachment(new ReadEqsResultNode { Id = Guid.NewGuid(), SensorVariableName = "scoutResult" }),
        };
        var mins = new Vector2[pills.Length]; const float W = 160f, H = 20f;
        void Draw()
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(700, 200));
            ImGui.Begin("##pills", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            var dl = ImGui.GetWindowDrawList();
            for (int i = 0; i < pills.Length; i++)
            {
                var min = new Vector2(10f + i * 172f, 20f); mins[i] = min;
                dl.AddRectFilled(min, min + new Vector2(W, H), ImGui.GetColorU32(theme.AttachmentCustomColor), 4f);
                draw.Invoke(null, new object[] { dl, ImGui.GetFont(), ImGui.GetFontSize(), min, H, 1f, pills[i], ImGui.GetColorU32(theme.TextDefault) });
            }
            ImGui.End();
        }
        for (int i = 0; i < 4; i++) frame.Step(Draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "colour-icons-pills.png"));

        var shot = Raylib.LoadImageFromScreen();
        try
        {
            for (int i = 0; i < pills.Length; i++)
            {
                int x0 = (int)mins[i].X, y0 = (int)mins[i].Y, x1 = x0 + (int)W, y1 = y0 + (int)H;
                int glyphPx = Count(shot, x0, y0, x1, y1, c => Near(c, pills[i].GlyphColor!.Value, 28));
                int labelPx = Count(shot, x0, y0, x1, y1, c => Near(c, theme.TextDefault, 28));
                Assert.True(glyphPx >= 8, $"pill {i}: the glyph is drawn in its icon colour ({glyphPx} px)");
                Assert.True(labelPx >= 8, $"pill {i}: the label stays the pill's text colour ({labelPx} px)");
            }
        }
        finally { Raylib.UnloadImage(shot); }
    }
}
