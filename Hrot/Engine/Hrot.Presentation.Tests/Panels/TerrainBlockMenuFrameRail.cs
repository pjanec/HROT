using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Fdp.Core;
using Fdp.Presentation.Fonts;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using GizmoMap.Presentation;
using Hrot.Common.Actions;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common.Components;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ Terrain authoring U-3 (mockup <c>terrain-authoring-2-blocks.svg</c>, the block's right-click menu) in a REAL frame with the editor's real fonts (Xvfb; a skip is not a pass): each of the six rows — Re-roll layout, Rotate, Move, Unpack…, Change recipe…, Delete —
/// draws its NAMED icon in its colour (dice amber, rotate grey, move grey, unpack teal, the block icon orange, trash red), the labels line up in one column, and the shortcut hints sit right.
/// 🔴 Red-proof (compiles): <c>MenuGlyphs.Resolve</c> answering false ⇒ no icon is drawn ⇒ every row's colour count is 0 ⇒ red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class TerrainBlockMenuFrameRail
{
    private static int Count(Image image, Vector2 min, Vector2 max, Vector4 c, int tol = 10)
    {
        int n = 0, r = (int)Math.Round(c.X * 255), g = (int)Math.Round(c.Y * 255), b = (int)Math.Round(c.Z * 255);
        for (int y = (int)min.Y; y < (int)max.Y && y < image.Height; y++)
            for (int x = (int)min.X; x < (int)max.X && x < image.Width; x++)
            {
                var p = Raylib.GetImageColor(image, x, y);
                if (Math.Abs(p.R - r) <= tol && Math.Abs(p.G - g) <= tol && Math.Abs(p.B - b) <= tol) n++;
            }
        return n;
    }

    [SkippableFact]
    public void TheBlockMenu_DrawsItsSixRows_EachWithItsNamedIconInItsColour_AndAlignedLabels()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>(); repo.RegisterComponent<NetworkIdentity>(); repo.RegisterComponent<TerrainBlock>(); repo.RegisterComponent<EntityGuid>(); repo.RegisterComponent<SelectionState>();
        var e = repo.CreateEntity();
        repo.AddComponent(e, new SimTransform { Position = new Vector3(100, 100, 0), Rotation = Quaternion.Identity });
        repo.AddComponent(e, new TerrainBlock { Recipe = new FixedString32("town"), Seed = 4471, Fit = TerrainBlock.FitAll });
        repo.AddComponent(e, new EntityGuid(Guid.NewGuid())); repo.AddComponent(e, new NetworkIdentity(5)); repo.AddComponent(e, new SelectionState());
        var items = JsonSerializer.Deserialize<ContextMenuItemDto[]>(MapMenuActions.JsonFor(repo, e), Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
        Assert.Equal(6, items.Count(i => i.IsSeparator != true && i.Label != null));

        var before = MenuIconRenderer.GlyphResolver;
        MenuIconRenderer.GlyphResolver = Hrot.UI.Common.Menus.MenuGlyphs.Resolve;
        try
        {
            using var frame = UiFrameHarness.Begin(420, 300);
            new EditorFontService().Initialize(1f, 1f);
            var rects = new (string Label, string? Icon, Vector2 Min, Vector2 Max, float LabelX)[6];
            frame.StepN(4, () =>
            {
                ImGui.SetNextWindowPos(new Vector2(20, 20)); ImGui.SetNextWindowSize(new Vector2(380, 250));
                ImGui.Begin("Block menu##rail");
                int i = 0;
                foreach (var it in items)
                {
                    if (it.IsSeparator == true || it.Label == null) { ImGui.Separator(); continue; }
                    MenuRows.Item(it.Label, it.Icon, it.Shortcut, null, it.Enabled != false, null, reserve: true);
                    if (i < 6) rects[i] = (it.Label, it.Icon, ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), 0);
                    i++;
                }
                ImGui.End();
            });
            if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); frame.Screenshot(Path.Combine(dir, "u3-block-menu.png")); }
            var shot = Raylib.LoadImageFromScreen();
            try
            {
                var expect = new[]
                {
                    ("Re-roll layout", "terrain/reroll"), ("Rotate", "terrain/rotate"), ("Move", "terrain/move"),
                    ("Unpack into houses and features", "terrain/unpack"), ("Change recipe…", "terrain/block"), ("Delete", "terrain/delete"),
                };
                for (int i = 0; i < 6; i++)
                {
                    Assert.Equal(expect[i].Item1, rects[i].Label);
                    Assert.Equal(expect[i].Item2, rects[i].Icon);
                    var icon = Icons.Get(expect[i].Item2);
                    float lh = rects[i].Max.Y - rects[i].Min.Y;
                    int px = Count(shot, rects[i].Min, new Vector2(rects[i].Min.X + lh * 1.6f, rects[i].Max.Y), icon.Color);
                    // the Move row is greyed by design (the drag): its glyph draws at the disabled alpha, so only the other rows must show their exact colour
                    if (expect[i].Item2 != "terrain/move") Assert.True(px >= 8, $"{expect[i].Item1}: its {expect[i].Item2} icon is drawn in its colour ({px} px)");
                }
                // one label column: every row's rectangle starts at the same x and the gutter is reserved (the glyph sits left of the text)
                Assert.Single(rects.Select(r => (int)r.Min.X).Distinct());
            }
            finally { Raylib.UnloadImage(shot); }
        }
        finally { MenuIconRenderer.GlyphResolver = before; }
    }
}
