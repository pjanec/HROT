using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Fonts;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using Raylib_cs;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Browser;

/// <summary>
/// ⭐ U-4 (docs/UX/DESIGN_UI_Conventions.md §2 + §6 C2) — the asset browser's per-kind tree is the shared <c>TreeRow</c>: a folder is <b>chevron · icon · label</b>, a leaf (an asset) keeps the chevron's slot, so its icon and label start
/// where a sibling folder's do, and depth is the indent. Read off a real frame (real fonts, Xvfb); folders are magenta icons, assets cyan. 🔴 Red-proof (compiles): <c>DrawLeafRow</c> drawing its row at depth 0 ⇒ nested leaves lose their indent ⇒ red.
/// Render (never in git): <c>HROT_RAIL_SHOTS</c> ⇒ <c>tree-rows-assets.png</c>.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheAssetBrowserTreeDrawsTreeRowsTests
{
    private static readonly Color FolderColour = new(255, 0, 255, 255), LeafColour = new(0, 255, 255, 255);

    private sealed class Asset : IEditableAsset
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name { get; init; } = "Asset";
        public AssetKind Kind => AssetKind.Blueprint;
        public string SourceFilePath { get; init; } = "";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    private sealed class Catalog : IAssetCatalog
    {
        private readonly List<IEditableAsset> _all;
        public Catalog(params IEditableAsset[] all) => _all = all.ToList();
        public IReadOnlyList<IEditableAsset> All => _all;
        public IEditableAsset? FindByAssetId(Guid id) => _all.FirstOrDefault(a => a.AssetId == id);
        public IEditableAsset? FindByName(string name) => _all.FirstOrDefault(a => a.Name == name);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid id) => Array.Empty<IEditableAsset>();
#pragma warning disable CS0067
        public event Action<AssetKind>? Changed;
#pragma warning restore CS0067
    }

    private sealed class Icons : IIconProvider
    {
        public nint Folder, Leaf;
        public bool TryGet(string key, out IconHandle handle)
        {
            handle = new IconHandle(key is "folder" or "folder_open" ? Folder : Leaf, 16, 16);
            return true;
        }
    }

    private static Texture2D Swatch(Color c) { var i = Raylib.GenImageColor(16, 16, c); var t = Raylib.LoadTextureFromImage(i); Raylib.UnloadImage(i); return t; }

    [SkippableFact]
    public void TheAssetTree_DrawsChevronIconLabel_ALeafKeepsTheSlot_DepthIsTheIndent()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string baseFolder = AssetRoots.AssetsFor(AssetKind.Blueprint);
        var catalog = new Catalog(
            new Asset { Name = "Deep", SourceFilePath = Path.Combine(baseFolder, "combat", "inner", "Deep.bp.json") },
            new Asset { Name = "Guard", SourceFilePath = Path.Combine(baseFolder, "combat", "Guard.bp.json") },
            new Asset { Name = "Patrol", SourceFilePath = Path.Combine(baseFolder, "Patrol.bp.json") });

        using var frame = UiFrameHarness.Begin(600, 360);
        new EditorFontService().Initialize(1f, 1f);
        var folder = Swatch(FolderColour); var leaf = Swatch(LeafColour);
        try
        {
            var panel = new AssetBrowserPanel(catalog, new Icons { Folder = (nint)folder.Id, Leaf = (nint)leaf.Id },
                new AssetBrowserPanelOptions { Kinds = AssetKindFilter.Blueprint, ShowAllTab = false, InitialKind = AssetKind.Blueprint, InitialFullPath = "combat/inner/Deep.bp.json" }, null);
            for (int i = 0; i < 10; i++)
                frame.Step(() =>
                {
                    ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(500, 300));
                    ImGui.Begin("Assets##u4", ImGuiWindowFlags.NoSavedSettings);
                    panel.DrawContent();
                    ImGui.End();
                });
            if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(Path.Combine(dir, "tree-rows-assets.png"));

            var shot = Raylib.LoadImageFromScreen();
            try
            {
                var bg = Raylib.GetImageColor(shot, 300, 250);   // inside the window, below the rows
                var folders = TreeRowProbe.Rows(shot, 16, 36, 500, 300, FolderColour, bg);
                var leaves = TreeRowProbe.Rows(shot, 16, 36, 500, 300, LeafColour, bg).OrderBy(r => r.Y0).ToList();
                Assert.True(folders.Count == 2 && leaves.Count == 3, $"folders combat + inner, leaves Deep + Guard + Patrol ({folders.Count}, {leaves.Count})");
                float indent = ImGui.GetStyle().IndentSpacing;
                var combat = folders[0]; var inner = folders[1];
                Assert.True(combat.ChevronMax >= 0 && combat.ChevronMax < combat.IconMin && combat.IconMax < combat.LabelMin, "a folder: chevron < icon < label");
                Assert.True(inner.ChevronMax >= 0 && inner.ChevronMax < inner.IconMin && inner.IconMax < inner.LabelMin, "a nested folder too");
                Assert.True(leaves.All(l => l.ChevronMax < 0 && l.IconMax < l.LabelMin), "an asset draws icon then label and no arrow");
                var deep = leaves[0]; var guard = leaves[1]; var patrol = leaves[2];
                Assert.True(Math.Abs(inner.IconMin - combat.IconMin - indent) <= 1.5f, "depth is the indent");
                Assert.True(Math.Abs(guard.IconMin - inner.IconMin) <= 1 && Math.Abs(guard.LabelMin - inner.LabelMin) <= 1, $"Guard (depth 1) keeps the chevron's slot: at {guard.IconMin}/{guard.LabelMin} like the folder inner {inner.IconMin}/{inner.LabelMin}");
                Assert.True(Math.Abs(patrol.IconMin - combat.IconMin) <= 1 && Math.Abs(patrol.LabelMin - combat.LabelMin) <= 1, "Patrol (depth 0) shares the depth-0 folder's x");
                Assert.True(Math.Abs(deep.IconMin - inner.IconMin - indent) <= 1.5f, "Deep (depth 2) is one indent deeper than its folder row");
            }
            finally { Raylib.UnloadImage(shot); }
        }
        finally { Raylib.UnloadTexture(folder); Raylib.UnloadTexture(leaf); }
    }
}
