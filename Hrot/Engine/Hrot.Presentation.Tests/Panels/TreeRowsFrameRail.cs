using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Fonts;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using NodeEditor.Core;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Dialogs;
using NodeEditor.UI.Picker;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ U-4 (docs/UX/DESIGN_UI_Conventions.md §2 + §6 C2, mockup ui-conventions-visual.svg §①) — the REAL tree drawers draw <b>chevron · icon · label</b> through the shared <c>TreeRow</c>, read off a real frame (real fonts, Xvfb):
/// the picker's Tree layout (<c>TreeLayout</c>) and the Save-As dialog's folder tree (<c>SaveAsBrowserDialog</c>). The icons are solid colours (folders magenta, leaves cyan) so each row is found exactly;
/// a folder's chevron is left of its icon, its icon left of its label, a LEAF keeps the chevron's slot (its icon and label start where a sibling folder's do) and the depth is the indent.
/// 🔴 Red-proof (compiles): <c>TreeLayout</c>'s leaf not adding the chevron slot (<c>textX = pos.X + 4f</c>, as before U-4) ⇒ a leaf's icon left of its sibling folder's ⇒ red.
/// Renders (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>tree-rows-picker.png</c>, <c>tree-rows-saveas.png</c>.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class TreeRowsFrameRail
{
    private static readonly Color FolderColour = new(255, 0, 255, 255), LeafColour = new(0, 255, 255, 255);

    private sealed class Icons : IIconProvider
    {
        public nint Folder, Leaf;
        public bool TryGet(string key, out IconHandle handle)
        {
            nint tex = key is "folder" or "folder_open" ? Folder : key == "leaf" ? Leaf : 0;
            handle = new IconHandle(tex, 16, 16);
            return tex != 0;
        }
    }

    private static Texture2D Swatch(Color c) { var i = Raylib.GenImageColor(16, 16, c); var t = Raylib.LoadTextureFromImage(i); Raylib.UnloadImage(i); return t; }

    private static void Shoot(UiFrameSession frame, string name)
    {
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, name));
    }

    /// <summary>The geometry every tree must show; <paramref name="folders"/> = the folder rows top to bottom, <paramref name="leaves"/> = the leaf rows.</summary>
    private static void AssertGeometry(List<TreeRowProbe.Row> folders, List<TreeRowProbe.Row> leaves, float indent, string what)
    {
        foreach (var f in folders)
        {
            Assert.True(f.ChevronMax >= 0 && f.ChevronMax < f.IconMin, $"{what}: a folder's chevron ({f.ChevronMax}) is left of its icon ({f.IconMin})");
            Assert.True(f.IconMax < f.LabelMin, $"{what}: its icon ({f.IconMax}) is left of its label ({f.LabelMin})");
        }
        foreach (var l in leaves)
        {
            Assert.True(l.ChevronMax < 0, $"{what}: a leaf draws no arrow");
            Assert.True(l.IconMax < l.LabelMin, $"{what}: a leaf's icon is left of its label");
        }
    }

    // ── the picker's Tree layout ───────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void ThePickerTree_DrawsChevronIconLabel_ALeafKeepsTheChevronSlot_DepthIsTheIndent()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(700, 420);
        new EditorFontService().Initialize(1f, 1f);
        var folder = Swatch(FolderColour); var leaf = Swatch(LeafColour);
        try
        {
            var icons = new Icons { Folder = (nint)folder.Id, Leaf = (nint)leaf.Id };
            var registry = new PickerRegistry();
            registry.SetServices(icons, new DefaultTheme());
            PickerEntry E(string id, string name, string? cat) => new(id, name, null, cat, null, null, null, IconKey: "leaf");
            registry.OpenPicker(new PickerRequest
            {
                ContextKey = "rail.tree", Title = "Tree rail", Layout = PickerLayout.Tree, SelectionMode = PickerSelectionMode.Single,
                AnchorScreen = new Vector2(20, 20), DefaultSize = new Vector2(520, 380), InitialSelectionId = "bl",
                ItemsProvider = () => new[] { E("bl", "BetaLeaf", "Alpha/Beta"), E("al", "AlphaLeaf", "Alpha"), E("tl", "TopLeaf", null) },
            }, _ => { });
            for (int i = 0; i < 10; i++) frame.Step(registry.DrawFrame);
            Shoot(frame, "tree-rows-picker.png");

            var shot = Raylib.LoadImageFromScreen();
            try
            {
                var bg = Raylib.GetImageColor(shot, 300, 330);   // inside the picker window, below the rows
                var folders = TreeRowProbe.Rows(shot, 28, 40, 520, 400, FolderColour, bg);
                var leaves = TreeRowProbe.Rows(shot, 28, 40, 520, 400, LeafColour, bg);
                Assert.True(folders.Count == 2 && leaves.Count == 3, $"two folder rows and three leaf rows are drawn ({folders.Count}, {leaves.Count})");
                float indent = ImGui.GetStyle().IndentSpacing;
                AssertGeometry(folders, leaves.OrderBy(l => l.Y0).Skip(1).ToList(), indent, "picker");   // (the first leaf is the pre-selected one: ImGui's focus rectangle is drawn at its left edge)
                var alpha = folders[0]; var beta = folders[1];
                var ordered = leaves.OrderBy(l => l.Y0).ToList();
                var betaLeafRow = ordered[0]; var alphaLeaf = ordered[1]; var topLeaf = ordered[2];
                Assert.True(Math.Abs(beta.IconMin - alpha.IconMin - indent) <= 1.5f, $"depth is the indent: Beta's icon is {indent} right of Alpha's ({beta.IconMin - alpha.IconMin})");
                Assert.True(Math.Abs(alphaLeaf.IconMin - beta.IconMin) <= 1 && Math.Abs(alphaLeaf.LabelMin - beta.LabelMin) <= 1, $"a leaf keeps the chevron's slot: AlphaLeaf at {alphaLeaf.IconMin}/{alphaLeaf.LabelMin} == its sibling folder Beta at {beta.IconMin}/{beta.LabelMin}");
                Assert.True(Math.Abs(topLeaf.IconMin - alpha.IconMin) <= 1 && Math.Abs(topLeaf.LabelMin - alpha.LabelMin) <= 1, "TopLeaf shares the x of the depth-0 folder");
                Assert.True(Math.Abs(betaLeafRow.IconMin - beta.IconMin - indent) <= 1.5f, "BetaLeaf, one level deeper, is one indent right");
            }
            finally { Raylib.UnloadImage(shot); }
        }
        finally { Raylib.UnloadTexture(folder); Raylib.UnloadTexture(leaf); }
    }

    // ── the Save-As dialog's folder tree ──────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void TheSaveAsFolderTree_DrawsChevronIconLabel_ALeafFolderKeepsTheChevronSlot_DepthIsTheIndent()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(900, 520);
        new EditorFontService().Initialize(1f, 1f);
        var folder = Swatch(FolderColour);
        try
        {
            var icons = new Icons { Folder = (nint)folder.Id };
            var dialog = new SaveAsBrowserDialog();
            var root = new CategoryNode("", new[]
            {
                new CategoryNode("Alpha", new[] { new CategoryNode("Beta", Array.Empty<CategoryNode>()), new CategoryNode("Gamma", new[] { new CategoryNode("Delta", Array.Empty<CategoryNode>()) }) }),
                new CategoryNode("Omega", Array.Empty<CategoryNode>()),
            });
            dialog.Open(new SaveAsRequest { Title = "Save as", GetFolderTree = () => root, InitialName = "x" }, _ => { });
            for (int i = 0; i < 10; i++) frame.Step(() => dialog.DrawFrame(icons));
            Shoot(frame, "tree-rows-saveas.png");

            var shot = Raylib.LoadImageFromScreen();
            try
            {
                var bg = Raylib.GetImageColor(shot, 200, 450);   // inside the folders pane, below the rows
                var rows = TreeRowProbe.Rows(shot, 72, 90, 340, 500, FolderColour, bg).Where(r => r.IconMin < 330).ToList();   // the left (folders) pane
                Assert.True(rows.Count >= 5, $"Alpha, Beta, Gamma, Delta, Omega are drawn ({rows.Count} rows)");
                float indent = ImGui.GetStyle().IndentSpacing;
                var alpha = rows[0]; var beta = rows[1]; var gamma = rows[2]; var delta = rows[3]; var omega = rows[4];
                Assert.True(alpha.ChevronMax >= 0 && alpha.ChevronMax < alpha.IconMin && alpha.IconMax < alpha.LabelMin, "Alpha (has children): chevron < icon < label");
                Assert.True(gamma.ChevronMax >= 0 && gamma.ChevronMax < gamma.IconMin, "Gamma (has children): chevron < icon");
                Assert.True(beta.ChevronMax < 0 && delta.ChevronMax < 0 && omega.ChevronMax < 0, "a folder with no children draws no arrow...");
                Assert.True(Math.Abs(beta.IconMin - gamma.IconMin) <= 1 && Math.Abs(beta.LabelMin - gamma.LabelMin) <= 1, "...but keeps the chevron's slot: Beta (no arrow) and Gamma (arrow) share their icon and label x");
                Assert.True(Math.Abs(omega.IconMin - alpha.IconMin) <= 1 && Math.Abs(omega.LabelMin - alpha.LabelMin) <= 1, "Omega (no arrow) shares the depth-0 x with Alpha");
                Assert.True(Math.Abs(beta.IconMin - alpha.IconMin - indent) <= 1.5f && Math.Abs(delta.IconMin - gamma.IconMin - indent) <= 1.5f, "depth is the indent, one IndentSpacing per level");
            }
            finally { Raylib.UnloadImage(shot); }
        }
        finally { Raylib.UnloadTexture(folder); }
    }
}
