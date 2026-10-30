using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Xunit;
using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// CE-1026 — the main menu's TOP-LEVEL order. It used to be first-registration order, and the window manager registers
/// <c>Settings</c> in its own constructor, so <c>Settings</c> came first and <c>File</c> later. Now File, Edit, View, … lead.
/// ⭐ CE-4085 (🔒 user's testing finding) — <b>Help is last and drawn at the RIGHT edge; Settings and Perspective are SUBMENUS of View</b>
/// (View ▸ Settings ▸ …, View ▸ Perspective ▸ …). ⛔ SUPERSEDED: the CE-1243 order … Debug · Perspective · Settings · Windows · Help.
/// </summary>
public class TopLevelMenuOrderTests
{
    private static WM Wm() => new(new IconAtlas(System.IntPtr.Zero, 128f, 128f, 16f));

    private sealed class Blank : ManagedWindow
    {
        public Blank(string id, string perspective) : base(id, id, perspective, WindowScope.PerspectiveBound) { }
        protected override void DrawClientArea() { }
    }

    /// <summary>A real window manager with two claimed perspectives, so the Perspective switcher has items.</summary>
    private static WM WmWithPerspectives()
    {
        var wm = Wm();
        wm.RegisterWindow(new Blank("w-IG", "IG"));
        wm.RegisterWindow(new Blank("w-Scn", "Scenario"));
        wm.SwitchPerspective("Scenario");
        return wm;
    }

    [Fact]
    public void CE1026_FileLeads_EvenWhenSettingsRegisteredFirst()
    {
        var reg = new GlobalMenuRegistry();
        reg.RegisterItem("View/Settings/UI Scale & Fonts…", () => { });
        reg.RegisterItem("Tools/Radar", () => { });
        reg.RegisterItem("File/Open", () => { });
        reg.RegisterItem("Edit/Undo", () => { });

        Assert.Equal(new[] { "File", "Edit", "View", "Tools" }, reg.TopLevelMenus().Select(m => m.Name));
    }

    /// <summary>⭐ CE-4114 — an UNLISTED root segment is drawn AFTER the rightmost listed non-Help menu (Debug), registration order among themselves,
    /// and before Help. ⛔ SUPERSEDED: "an unlisted menu ranks just before Windows". ✅ Red-proof: <c>UnlistedRank</c> returning <c>TopLevelRank("Help") - 5</c> ⇒ Mission lands before Debug ⇒ red.</summary>
    [Fact]
    public void CE4114_AnUnlistedMenu_LandsAfterTheRightmostListedNonHelpMenu_NotBesideHelp()
    {
        var reg = new GlobalMenuRegistry();
        reg.RegisterItem("Help/A", () => { });
        reg.RegisterItem("Zeta/A", () => { });
        reg.RegisterItem("Alpha/A", () => { });
        reg.RegisterItem("Mission/Brief", () => { });
        reg.RegisterItem("Debug/D", () => { });
        reg.RegisterItem("File/A", () => { });

        Assert.Equal(new[] { "File", "Debug", "Zeta", "Alpha", "Mission", "Help" }, reg.TopLevelMenus().Select(m => m.Name));
        Assert.False(GlobalMenuRegistry.IsRightAligned("Mission"));
        Assert.True(GlobalMenuRegistry.IsRightAligned("Help"));
    }

    [Fact]
    public void CE1026_TheSnapshot_ListsTheMenusInBarOrder()
    {
        var reg = new GlobalMenuRegistry();
        reg.RegisterItem("Help/About", () => { });
        reg.RegisterItem("View/Settings/UI Scale & Fonts…", () => { });
        reg.RegisterItem("File/Open", () => { });
        reg.RegisterItem("File/Save", () => { });

        var paths = reg.BuildViewModel("Editor").Items.Select(i => i.Path).ToList();
        Assert.Equal(new[] { "File/Open", "File/Save", "View/Settings/UI Scale & Fonts…", "Help/About" }, paths);
    }

    [Fact]
    public void CE1026_TheWindowManagersOwnSettings_DoesNotLeadAHostsFile()
    {
        var wm = Wm();
        wm.GlobalMenu.RegisterItem("File/Open Asset…", () => { });

        Assert.Equal("File", wm.GlobalMenu.TopLevelMenus().First().Name);
    }

    /// <summary>⭐ CE-4085 cond. 1 — the bar the ONE tree computes ends with Help, and the model marks Help (only Help) right-aligned; the
    /// draw path obeys <see cref="BarMenu.RightAligned"/>. ✅ Red-proof: <c>Help</c> put before <c>Windows</c> in <c>TopLevelOrder</c> ⇒ red;
    /// <c>RightAligned</c> returning false ⇒ red.</summary>
    [Fact]
    public void CE4085_HelpIsLast_AndMarkedRightAligned_OnTheOneTree()
    {
        var wm = WmWithPerspectives();
        wm.GlobalMenu.RegisterItem("File/Open", () => { });
        wm.GlobalMenu.RegisterItem("Zeta/Unlisted", () => { });

        var bar = wm.GlobalMenu.BarMenus(wm.GlobalMenu.CollectContributions(wm.CurrentPerspective));

        Assert.Equal(new[] { "File", "View", "Zeta", "Help" }, bar.Select(m => m.Name));   // ⭐ CE-4114: Windows is under View; Zeta (unlisted) after the listed menus
        Assert.Equal("Help", bar.Last().Name);
        Assert.True(bar.Last().RightAligned);
        Assert.Equal(new[] { "Help" }, bar.Where(m => m.RightAligned).Select(m => m.Name));
        Assert.True(GlobalMenuRegistry.IsRightAligned("Help"));
        Assert.False(GlobalMenuRegistry.IsRightAligned("Zeta"));
    }

    /// <summary>⭐ CE-4114 — <c>Windows</c> is a SUBMENU of <c>View</c>: no top-level Windows; <c>View/Windows/&lt;perspective&gt;/&lt;window&gt;</c> is reachable and nothing is lost.
    /// ✅ Red-proof: <c>WindowsMenuDto</c> labelled "Windows" again (top-level) ⇒ red.</summary>
    [Fact]
    public void CE4114_Windows_IsASubmenuOfView_NotTopLevel()
    {
        var wm = WmWithPerspectives();
        var bar = wm.GlobalMenu.BarMenus(wm.GlobalMenu.CollectContributions(wm.CurrentPerspective));
        Assert.DoesNotContain(bar, m => m.Name == "Windows");

        var view = bar.Single(m => m.Name == "View");
        var windows = view.Parts.SelectMany(p => p.Item.Children ?? System.Array.Empty<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto>())
            .Single(c => c.Label == "Windows");
        var leaves = windows.Children!.SelectMany(g => g.Children ?? System.Array.Empty<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto>()).Select(c => c.Label);
        Assert.Contains("w-IG", leaves);
        Assert.Contains("w-Scn", leaves);
        Assert.Equal("View/Windows", GlobalMenuRegistry.WindowsSubmenu);
    }

    /// <summary>⭐ CE-4114 — Help is ICON-ONLY: its draw label has no visible text, and a real frame asks the icon resolver for the question-mark key (and
    /// not for the old info badge). ✅ Red-proof: <c>BarMenu.IconOnly</c> returning false ⇒ the label is "Help" ⇒ red; <c>HelpIconKey</c> back to "status/info" ⇒ red.
    /// Display rail: needs the ImGui fixture (native cimgui); skipped-vs-run is reported by the runner.</summary>
    [Fact]
    public void CE4114_Help_IsAnIconOnlyEntry_ARealFrameDrawsTheQuestionMark()
    {
        var wm = WmWithPerspectives();
        var seen = new System.Collections.Generic.List<string>();
        using var fixture = new Fdp.Presentation.Tests.ImGuiTestFixture();
        wm.MenuIcons = (string key, out nint tex, out System.Numerics.Vector2 uv0, out System.Numerics.Vector2 uv1) =>
        {
            seen.Add(key);
            tex = ImGuiNET.ImGui.GetIO().Fonts.TexID; uv0 = System.Numerics.Vector2.Zero; uv1 = System.Numerics.Vector2.One;
            return tex != 0;
        };

        var help = wm.GlobalMenu.BarMenus(wm.GlobalMenu.CollectContributions(wm.CurrentPerspective)).Single(m => m.Name == "Help");
        Assert.True(help.IconOnly);
        Assert.StartsWith("##", help.DrawLabel);                 // no visible text: ImGui hides everything after "##" and the label is empty
        Assert.DoesNotContain(wm.GlobalMenu.BarMenus(wm.GlobalMenu.CollectContributions(wm.CurrentPerspective)).Where(m => m.Name != "Help"), m => m.IconOnly);

        fixture.NewFrame();
        wm.Render();
        fixture.Render();

        Assert.Contains(WM.HelpIconKey, seen);
        Assert.Equal("help", WM.HelpIconKey);
    }

    /// <summary>⭐ CE-4085 cond. 2 — no top-level Settings, no top-level Perspective; View holds a Settings submenu (every item that was under
    /// Settings) and a Perspective submenu (the switcher, checked state kept). ✅ Red-proof: the WM registering <c>Settings/…</c> top-level
    /// ⇒ red; the Perspective DTO labelled top-level again ⇒ red.</summary>
    [Fact]
    public void CE4085_SettingsAndPerspective_AreSubmenusOfView_NotTopLevel()
    {
        var wm = WmWithPerspectives();
        // a host's own settings items join the same View ▸ Settings submenu through the registry's path constant
        wm.GlobalMenu.RegisterItem(GlobalMenuRegistry.SettingsMenuPath + "/Layout/Save current as default", () => { });

        var contributions = wm.GlobalMenu.CollectContributions(wm.CurrentPerspective);
        var bar = wm.GlobalMenu.BarMenus(contributions);

        Assert.DoesNotContain(bar, m => m.Name == "Settings");
        Assert.DoesNotContain(bar, m => m.Name == "Perspective");

        var view = bar.Single(m => m.Name == "View");
        var settings = view.Node!.Children["Settings"];
        Assert.Contains("UI Scale & Fonts…", settings.Children.Keys);
        Assert.Contains("Layout", settings.Children.Keys);

        var perspective = view.Parts.SelectMany(p => p.Item.Children ?? System.Array.Empty<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto>())
            .Single(c => c.Label == "Perspective");
        Assert.Equal(new[] { "IG", "Scenario" }, perspective.Children!.Select(c => c.Label).OrderBy(x => x));
        Assert.Equal(new[] { "Scenario" }, perspective.Children!.Where(c => c.IsChecked == true).Select(c => c.Label));
    }

    /// <summary>⭐ CE-4085 cond. 3 — NO ITEM LOST: the leaf set of a real window manager's tree equals the pre-CE-4085 leaf set with
    /// Settings/… and Perspective/… re-parented under View — nothing dropped, nothing duplicated, no other menu touched.
    /// ✅ Red-proof: dropping the "UI Scale &amp; Fonts…" registration, or a perspective from the switcher ⇒ red.</summary>
    [Fact]
    public void CE4085_NoItemIsLost_TheLeafSetIsTheOldSetReparentedUnderView()
    {
        var wm = WmWithPerspectives();
        wm.GlobalMenu.RegisterItem("File/Open", () => { });
        wm.GlobalMenu.RegisterItem("View/Grid", () => { });
        wm.GlobalMenu.RegisterItem(GlobalMenuRegistry.SettingsMenuPath + "/Layout/Reset", () => { });   // a host's settings item

        // BEFORE (CE-1243): top-level Settings and Perspective menus.
        var before = new[]
        {
            "File/Open", "View/Grid",
            "Settings/UI Scale & Fonts…", "Settings/Layout/Reset",
            "Perspective/IG", "Perspective/Scenario",
            "Windows/IG/w-IG", "Windows/Scenario/w-Scn",
            "Help/About",
        };
        string Rehome(string p) => p.StartsWith("Settings/") || p.StartsWith("Perspective/") || p.StartsWith("Windows/") ? "View/" + p : p;   // ⭐ CE-4114: + Windows

        var after = wm.GlobalMenu.BuildViewModel(wm.CurrentPerspective).Items
            .Where(i => i.Kind == "item").Select(i => i.Path).ToHashSet();
        var expected = before.Select(Rehome).ToHashSet();

        Assert.Equal(expected.OrderBy(x => x), after.OrderBy(x => x));
        Assert.DoesNotContain(after, p => p.StartsWith("Settings/") || p.StartsWith("Perspective/"));
    }

    /// <summary>⭐ CE-4085 — a real ImGui frame draws the re-ordered bar (both passes: left-flowing menus, then Help at the right edge)
    /// without throwing.</summary>
    [Fact]
    public void CE4085_ARealFrame_DrawsTheReorderedBar()
    {
        var wm = WmWithPerspectives();
        using var fixture = new Fdp.Presentation.Tests.ImGuiTestFixture();
        fixture.NewFrame();
        wm.Render();
        fixture.Render();
    }
}
