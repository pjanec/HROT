using System.Linq;
using Fdp.Presentation.WindowManager;
using Xunit;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// CE-1026 — the main menu's TOP-LEVEL order. It used to be first-registration order, and the window manager registers
/// <c>Settings</c> in its own constructor, so <c>Settings</c> came first and <c>File</c> later. Now File, Edit, View, … lead,
/// the Perspective menu follows, then Settings and after.
/// </summary>
public class TopLevelMenuOrderTests
{
    [Fact]
    public void CE1026_FileLeads_EvenWhenSettingsRegisteredFirst()
    {
        var reg = new GlobalMenuRegistry();
        reg.RegisterItem("Settings/UI Scale & Fonts…", () => { });
        reg.RegisterItem("Tools/Radar", () => { });
        reg.RegisterItem("File/Open", () => { });
        reg.RegisterItem("Edit/Undo", () => { });

        Assert.Equal(new[] { "File", "Edit", "Tools", "Settings" }, reg.TopLevelMenus().Select(m => m.Name));
    }

    [Fact]
    public void CE1026_AnUnlistedMenu_LeadsBeforeSettings_AndTiesKeepRegistrationOrder()
    {
        var reg = new GlobalMenuRegistry();
        reg.RegisterItem("Settings/A", () => { });
        reg.RegisterItem("Zeta/A", () => { });
        reg.RegisterItem("Alpha/A", () => { });
        reg.RegisterItem("File/A", () => { });

        Assert.Equal(new[] { "File", "Zeta", "Alpha", "Settings" }, reg.TopLevelMenus().Select(m => m.Name));
        Assert.True(GlobalMenuRegistry.IsLeadingMenu("Zeta"));
        Assert.True(GlobalMenuRegistry.IsLeadingMenu("File"));
        Assert.False(GlobalMenuRegistry.IsLeadingMenu("Settings"));
        Assert.False(GlobalMenuRegistry.IsLeadingMenu("Windows"));
        Assert.False(GlobalMenuRegistry.IsLeadingMenu("Help"));
    }

    [Fact]
    public void CE1026_TheSnapshot_ListsTheMenusInBarOrder()
    {
        var reg = new GlobalMenuRegistry();
        reg.RegisterItem("Settings/UI Scale & Fonts…", () => { });
        reg.RegisterItem("File/Open", () => { });
        reg.RegisterItem("File/Save", () => { });

        var paths = reg.BuildViewModel("Editor").Items.Select(i => i.Path).ToList();
        Assert.Equal(new[] { "File/Open", "File/Save", "Settings/UI Scale & Fonts…" }, paths);
    }

    [Fact]
    public void CE1026_TheWindowManagersOwnSettings_DoesNotLeadAHostsFile()
    {
        var wm = new Fdp.Presentation.WindowManager.WindowManager(new Fdp.Presentation.Icons.IconAtlas(System.IntPtr.Zero, 128f, 128f, 16f));
        wm.GlobalMenu.RegisterItem("File/Open Asset…", () => { });

        Assert.Equal("File", wm.GlobalMenu.TopLevelMenus().First().Name);
        Assert.Equal("Settings", wm.GlobalMenu.TopLevelMenus().Last().Name);
    }
}
