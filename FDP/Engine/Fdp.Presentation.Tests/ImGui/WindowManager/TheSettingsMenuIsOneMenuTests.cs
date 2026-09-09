using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.WindowManager;

/// <summary>
/// ⭐⭐⭐ <b><c>VC-2</c>'s rail — THERE IS EXACTLY ONE <c>Settings</c> MENU, AND HOSTS CAN EXTEND IT.</b>
/// 🔒 <b>User, visual check <c>2026-08-22</c>:</b> <i>"move <c>File ▸ Layout</c> to a <c>Settings</c>
/// main menu."</i>
///
/// <para>⛔⛔ <b>The measurement that shaped the fix.</b> 📐 <c>WindowManager.RenderMainMenuBar</c> draws
/// <b>two independent menu models</b> into one bar: <c>RenderGlobalMenu(GlobalMenu.Root)</c> — the
/// path-trie hosts register into — and <c>ImGuiMenuRenderer.DrawMenus(BuildHostMenuDtos())</c>, a fixed
/// DTO list. ⚠ <c>Settings</c> lived in the DTO list. ⇒ ⭐ registering <c>"Settings/Layout/…"</c>
/// through <c>GlobalMenu</c> — the obvious reading of the request — would have produced <b>TWO
/// top-level menus both labelled <c>Settings</c></b>, side by side, which is worse than the
/// <c>File ▸ Layout</c> it replaced.</para>
///
/// <para>⭐ So the framework's own item MOVED into <c>GlobalMenu</c> *(<c>R-13</c>)*, and these rails
/// pin both halves: the item is still there, and it is now in the model a host can extend.</para>
/// </summary>
public sealed class TheSettingsMenuIsOneMenuTests
{
    private static Fdp.Presentation.WindowManager.WindowManager Wm()
        => new(new IconAtlas(nint.Zero, 1, 1, 16f));

    /// <summary>
    /// ⭐⭐ <b>The framework's own Settings item survives the move.</b> ⚠ Not decoration: the point of
    /// routing rather than duplicating is that nothing is LOST, and <c>UI Scale &amp; Fonts…</c> is the
    /// only entry the framework itself contributes.
    /// </summary>
    [Fact]
    public void TheFrameworksSettingsItem_LivesInTheGlobalMenu()
    {
        var wm = Wm();

        Assert.True(wm.GlobalMenu.Root.Children.ContainsKey("Settings"));
        Assert.Contains("UI Scale & Fonts…", wm.GlobalMenu.Root.Children["Settings"].Children.Keys);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A host registering under <c>Settings/</c> EXTENDS that menu — it does not create a
    /// second one.</b> ⛔ This is the failure the measurement predicted, and the whole reason the
    /// framework item moved. ⚠ Asserted as *"one <c>Settings</c> node holding BOTH"*, because two
    /// top-level menus of the same name are indistinguishable from one in any assertion that only
    /// counts children.
    /// </summary>
    [Fact]
    public void AHostsSettingsItem_JoinsTheSameMenu()
    {
        var wm = Wm();

        wm.GlobalMenu.RegisterItem("Settings/Layout/Save current as default", () => { });

        var settings = wm.GlobalMenu.Root.Children["Settings"];
        Assert.Contains("UI Scale & Fonts…", settings.Children.Keys);
        Assert.Contains("Layout",            settings.Children.Keys);
        Assert.Single(wm.GlobalMenu.Root.Children.Keys.Where(k => k == "Settings"));
    }

    /// <summary>
    /// ⛔⛔ <b>The host-menu DTOs must NOT carry a <c>Settings</c> block any more.</b>
    /// ⚠ This is the half that actually forbids the two-menus-one-name regression: leaving the DTO
    /// block in place would keep <see cref="AHostsSettingsItem_JoinsTheSameMenu"/> green while the bar
    /// drew two menus. ⭐ Asserted through the PUBLIC menu model, so it is a claim about what the bar
    /// receives, not about the source.
    /// </summary>
    [Fact]
    public void TheHostMenuDtos_NoLongerCarryASettingsBlock()
        => Assert.DoesNotContain(Wm().HostMenuLabelsForRail(), label => label == "Settings");
}
