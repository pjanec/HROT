using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Xunit;

using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// ⭐⭐⭐ <b><c>UXI-05</c> — the menu FOLLOWS FOCUS.</b>
/// 📄 <c>docs/DESIGN_Cgf_Menu_Follows_Focus_Slice.md</c> §3 ① ② ⑤.
///
/// <para>⛔⛔ <b>Every claim here is SILENT when it breaks</b>, which is why they are railed rather than
/// eyeballed: a leaf that resolves the wrong binding runs the wrong action *(no error)*, a leaf that
/// resolves none is simply not drawn *(no error)*, and a submenu whose leaves all vanish still opens
/// *(no error, just an empty popup)*.</para>
///
/// <para>⚠ Headless: <see cref="GlobalMenuRegistry"/> is a plain trie and
/// <see cref="GlobalMenuRegistry.BuildViewModel"/> needs no ImGui frame. ⭐ The one thing that DOES need
/// the manager is <c>HasVisibleDescendant</c>, which reads <c>CurrentPerspective</c>.</para>
/// </summary>
[Collection("ImGui Sequential")]
public class TheMenuFollowsFocusTests
{
    /// <summary>⭐ Minimal window that CLAIMS a perspective — <c>SwitchPerspective</c> refuses an unclaimed
    /// name *(A0)*, so a menu test that switches must first make the name legal.</summary>
    private sealed class ClaimWindow : ManagedWindow
    {
        public ClaimWindow(string perspective)
            : base($"claim_{perspective}", perspective, perspective, WindowScope.PerspectiveBound) { }
        protected override void DrawClientArea() { }
    }

    private static WM CreateManager(params string[] perspectives)
    {
        var wm = new WM(new IconAtlas(new System.IntPtr(1), 256f, 256f, 16f));
        foreach (var p in perspectives) wm.RegisterWindow(new ClaimWindow(p));
        return wm;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE RESOLUTION: two bindings on ONE path, and the perspective picks.</b>
    ///
    /// <para>📐 This is the whole model in four assertions — a perspective-specific binding wins for its
    /// own perspective, the GLOBAL binding serves every other, and neither wipes the other
    /// *(last-write-wins is PER PERSPECTIVE, ⛔ not per node)*.</para>
    /// </summary>
    [Fact]
    public void One_path_two_bindings_and_the_perspective_picks()
    {
        var menu = new GlobalMenuRegistry();
        int global = 0, scenario = 0;

        menu.RegisterItem("File/Save", () => global++);
        menu.RegisterItem("File/Save", () => scenario++, perspective: "Scenario");

        var leaf = menu.Root.Children["File"].Children["Save"];

        // ⛔ BOTH survived — the second call must not have replaced the first.
        Assert.Equal(2, leaf.Bindings.Count);

        leaf.ResolveBinding("Scenario")!.OnClick!();
        Assert.Equal(0, global);
        Assert.Equal(1, scenario);

        leaf.ResolveBinding("Blueprint")!.OnClick!();   // no Blueprint binding ⇒ the GLOBAL one
        Assert.Equal(1, global);
        Assert.Equal(1, scenario);

        // ⭐ And re-registering the global action replaces ONLY the global binding.
        menu.RegisterItem("File/Save", () => global += 10);
        Assert.Equal(2, leaf.Bindings.Count);
        leaf.ResolveBinding("Scenario")!.OnClick!();
        Assert.Equal(2, scenario);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>NOT DRAWN, ⛔ not greyed</b> — ruling 49, by construction. A leaf bound only to another
    /// perspective resolves <see langword="null"/>, and the draw path <c>continue</c>s on null.
    /// </summary>
    [Fact]
    public void A_leaf_bound_only_to_another_perspective_resolves_nothing()
    {
        var menu = new GlobalMenuRegistry();
        menu.RegisterItem("Scenario/Validate…", () => { }, perspective: "Scenario");

        var leaf = menu.Root.Children["Scenario"].Children["Validate…"];

        Assert.NotNull(leaf.ResolveBinding("Scenario"));
        Assert.Null(leaf.ResolveBinding("Blueprint"));
        Assert.Null(leaf.ResolveBinding(null));
    }

    /// <summary>
    /// ⛔⛔ <b>NO DEAD HEADERS</b> — UXI-05 names this as the risk of the model: a submenu whose every leaf
    /// is filtered away would still draw its header and open onto nothing.
    /// </summary>
    [Fact]
    public void A_submenu_whose_leaves_all_vanish_is_skipped()
    {
        var wm   = CreateManager("Scenario", "Blueprint");
        var menu = wm.GlobalMenu;

        menu.RegisterItem("Scenario/Validate…", () => { }, perspective: "Scenario");
        menu.RegisterItem("File/Save",          () => { });

        var scenarioNode = menu.Root.Children["Scenario"];
        var fileNode     = menu.Root.Children["File"];

        wm.SwitchPerspective("Scenario");
        Assert.True(wm.HasVisibleDescendant(scenarioNode));
        Assert.True(wm.HasVisibleDescendant(fileNode));

        wm.SwitchPerspective("Blueprint");
        // ⛔ Every leaf under Scenario/ filtered away ⇒ the HEADER goes with them.
        Assert.False(wm.HasVisibleDescendant(scenarioNode));
        // ⭐ The global one is untouched by the switch.
        Assert.True(wm.HasVisibleDescendant(fileNode));
    }

    /// <summary>
    /// ⚠ <b>A separator alone never justifies a submenu.</b> ⛔ Otherwise a group whose items are all
    /// perspective-filtered leaves a header containing one horizontal rule.
    /// </summary>
    [Fact]
    public void A_separator_alone_does_not_keep_a_submenu_alive()
    {
        var wm = CreateManager("Blueprint");
        wm.GlobalMenu.RegisterSeparator("Tools/sep_only");

        wm.SwitchPerspective("Blueprint");
        Assert.False(wm.HasVisibleDescendant(wm.GlobalMenu.Root.Children["Tools"]));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE PANEL MODEL (item ⑤)</b> — what the conformance rail reads. ⛔ Before this the menu
    /// published nothing, so *"which File items does this host offer?"* was unanswerable headlessly and a
    /// cross-host verdict on it was impossible.
    ///
    /// <para>⚠ <c>visible</c> is evaluated with the SAME expression the draw filters on, so the dump
    /// cannot claim an item the bar would hide.</para>
    /// </summary>
    [Fact]
    public void The_panel_model_carries_the_paths_their_scopes_and_their_visibility()
    {
        var menu = new GlobalMenuRegistry();
        menu.RegisterItem("File/Save",          () => { });
        menu.RegisterItem("File/Save",          () => { }, perspective: "Scenario");
        menu.RegisterItem("Scenario/Validate…", () => { }, perspective: "Scenario");
        menu.RegisterSeparator("File/sep_1");

        var dump = menu.BuildViewModel("Blueprint").Dump();
        var items = (dump["items"] as JsonArray)!
            .ToDictionary(i => i!["path"]!.GetValue<string>(), i => i!);

        Assert.Equal("global-menu", dump["panelKind"]!.GetValue<string>());
        Assert.Equal("Blueprint",   dump["currentPerspective"]!.GetValue<string>());

        // ⭐ Both scopes are reported — ⛔ "bound twice" and "bound once" are different facts.
        Assert.Equal(new[] { "*", "Scenario" },
            (items["File/Save"]["scopes"] as JsonArray)!.Select(s => s!.GetValue<string>()).ToArray());
        Assert.True(items["File/Save"]["visible"]!.GetValue<bool>());

        // ⛔ Present in the trie, NOT visible under Blueprint — the distinction the dump exists for.
        Assert.False(items["Scenario/Validate…"]["visible"]!.GetValue<bool>());

        Assert.Equal("separator", items["File/sep_1"]["kind"]!.GetValue<string>());

        // ⛔ Intermediate nodes are structure, not offers.
        Assert.DoesNotContain("File", items.Keys);
    }
}
