using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Tests.ImGui;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Xunit;
using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// ⭐⭐ <c>CE-1243</c> (docs/UX/UX_Feature_Menu_Follows_Focus.md §MB; 🔒 user, 2026-10-22: <i>"shouldnt the manin menu bar rendering be
/// unified and shared?"</i>) — the bar is ONE tree: registered items and per-frame CONTRIBUTORS merged by top-level name, in one order,
/// one snapshot. The window manager's Windows / Perspective / Help are contributors like any host's gizmo menus.
/// </summary>
public sealed class TheMenuBarIsOneTreeTests
{
    private static IReadOnlyList<ContextMenuItemDto> Menu(string top, params string[] leaves)
        => new[] { new ContextMenuItemDto { Label = top, Children = leaves.Select((l, i) => new ContextMenuItemDto { Id = i + 1, Label = l }).ToArray() } };

    /// <summary>⭐ MB-2 — a contributed <c>View</c> and the tree's <c>View</c> are ONE bar menu, in CE-1026/CE-4085 order (Settings and Perspective are View submenus, Help last)
    /// menu between the leading menus and Settings. ✅ Red-proof: merging by contributor instead of by name ⇒ two View entries.</summary>
    [Fact]
    public void CE1243_AContributedMenu_MergesIntoTheTreesMenuOfTheSameName_InBarOrder()
    {
        var reg = new GlobalMenuRegistry();
        reg.RegisterItem("View/Settings/UI", () => { });
        reg.RegisterItem("View/Grid", () => { });
        reg.RegisterItem("File/Open", () => { });
        reg.RegisterContributor("help", () => Menu("Help", "About"), _ => { });
        reg.RegisterContributor("persp", () => Menu("View", "Scenario"), _ => { });
        reg.RegisterContributor("gizmo", () => Menu("View", "Tactical Map Layers..."), _ => { });

        var bar = reg.BarMenus(reg.CollectContributions("Scenario"));

        Assert.Equal(new[] { "File", "View", "Help" }, bar.Select(m => m.Name));
        var view = bar.Single(m => m.Name == "View");
        Assert.NotNull(view.Node);
        Assert.Equal(2, view.Parts.Count);   // ⭐ CE-4085: the perspective feed and the gizmo feed both land in the one View
    }

    /// <summary>⭐ MB-4 / UXI-05 — a contributor bound to a perspective offers its menus only while that perspective is current.</summary>
    [Fact]
    public void CE1243_APerspectiveBoundContributor_OffersNothingElsewhere()
    {
        var reg = new GlobalMenuRegistry();
        reg.RegisterContributor("ig-gizmo", () => Menu("View", "2-D / 3-D Map"), _ => { }, perspective: "IG");

        Assert.Empty(reg.CollectContributions("Scenario"));
        Assert.Single(reg.CollectContributions("IG"));
    }

    /// <summary>⭐ MB-2 — a contributor is asked ONCE a frame (a gizmo layer's feed is consumed by the asking): the bar and the snapshot
    /// share the one answer, and the snapshot lists the contributed leaves with the contributor's scope.</summary>
    [Fact]
    public void CE1243_TheSnapshot_ListsContributedLeaves_FromTheSameCollection()
    {
        var reg = new GlobalMenuRegistry();
        int asked = 0;
        reg.RegisterContributor("gizmo", () => { asked++; return Menu("View", "Tactical Map Layers..."); }, _ => { }, perspective: "IG");

        var once = reg.CollectContributions("IG");
        var vm = reg.BuildViewModel("IG", once);

        Assert.Equal(1, asked);
        var leaf = Assert.Single(vm.Items, i => i.Path == "View/Tactical Map Layers...");
        Assert.Equal(new[] { "IG" }, leaf.Scopes);
    }

    /// <summary>⭐ MB-3 — the window manager's own menus are contributors of its tree (⛔ no second model beside it), and the bar draws
    /// through ONE walk without throwing in a real ImGui frame.</summary>
    [Fact]
    public void CE1243_WindowsPerspectiveAndHelp_AreContributorsOfTheOneTree()
    {
        var wm = new WM(new IconAtlas(IntPtr.Zero, 1, 1));
        wm.RegisterWindow(new Blank("w1", "Scenario"));
        wm.SwitchPerspective("Scenario");
        Assert.Equal(new[] { "wm/perspective", "wm/windows", "wm/help" }, wm.GlobalMenu.Contributors.Select(c => c.Id));
        var bar = wm.GlobalMenu.BarMenus(wm.GlobalMenu.CollectContributions(wm.CurrentPerspective)).Select(m => m.Name).ToArray();
        Assert.Equal(new[] { "View", "Help" }, bar);   // ⭐ CE-4114: Windows is a submenu of View
        var viewParts = wm.GlobalMenu.BarMenus(wm.GlobalMenu.CollectContributions(wm.CurrentPerspective)).Single(m => m.Name == "View").Parts;
        Assert.Contains(viewParts, p => p.Item.Children!.Any(c => c.Label == "Windows"));   // View ▸ Windows, contributed by wm/windows

        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        wm.Render();
        fixture.Render();
    }

    private sealed class Blank : ManagedWindow
    {
        public Blank(string id, string perspective) : base(id, id, perspective, WindowScope.PerspectiveBound) { }
        protected override void DrawClientArea() { }
    }
}
