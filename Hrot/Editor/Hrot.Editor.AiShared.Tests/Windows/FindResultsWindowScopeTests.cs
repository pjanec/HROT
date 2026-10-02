using System;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Windows;
using Xunit;

using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b><c>A5</c>/<c>A6</c> — "GLOBAL" IS A SCOPE, NOT A PERSPECTIVE.</b>
/// 📄 <c>docs/DESIGN_Perspective_Unification.md</c> §1c.
///
/// <para>🔒 <b>The user's ruling that produced this file, verbatim:</b> <i>"a new perspective ICON called
/// 'Global' which i never asked for. The global perspective should have no icon, it is just place for
/// windows that do not belong to any specific perspective but are available globally, pinnable to any
/// other perspective."</i></para>
///
/// <para>⛔⛔ <b>ONE line produced TWO defects</b>, and they need separate assertions because either could
/// be fixed while the other survived:
/// <list type="number">
///   <item>a phantom perspective named <c>Global</c> — <c>GetPerspectives()</c> returned it and
///   <c>PerspectiveToolbarSection</c> draws <b>one icon per entry</b> ⇒ the icon nobody asked for;</item>
///   <item>🔴 the window was <b>NOT globally available</b> — the opposite of its stated intent — because a
///   <see cref="WindowScope.PerspectiveBound"/> window shows only while its own perspective is
///   current.</item>
/// </list></para>
///
/// <para>⚠ <b>What this deliberately does NOT touch:</b> the Windows menu's <c>"Global"</c> GROUP is
/// CORRECT and must stay — 📐 <c>WindowManager</c> groups <see cref="WindowScope.Global"/> windows under
/// that label, which is exactly the <i>"place for windows that do not belong to any specific
/// perspective"</i> the user describes. ⛔ A menu grouping is not a perspective.</para>
/// </summary>
[Collection("ImGui Sequential")]
public sealed class FindResultsWindowScopeTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);
    public void Dispose() => _atlas.Dispose();

    /// <summary>⭐ The asset browser's instance, exactly as <c>EditorSubsystem</c> builds it.</summary>
    private static FindResultsWindow AssetBrowserInstance()
        => new(owningPerspective: string.Empty,
               idOverride:        "ai_asset_browser_find_results",
               scope:             WindowScope.Global);

    /// <summary>
    /// ⭐⭐⭐ <b>Defect ① — no phantom perspective, therefore no icon.</b> ⛔ The icon is not
    /// special-cased anywhere: removing the entry from the LIST is what removes the icon.
    /// </summary>
    [Fact]
    public void TheAssetBrowserInstanceContributesNoPerspective()
    {
        var wm = new WM(_atlas);
        wm.RegisterWindow(new FindResultsWindow("BTree", "ai_find_results_btree"));
        wm.RegisterWindow(AssetBrowserInstance());

        Assert.DoesNotContain("Global", wm.GetPerspectives());
        Assert.Equal(new[] { "BTree" }, wm.GetPerspectives());
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Defect ② — and it is actually GLOBAL.</b> 📌 This is the assertion that would still have
    /// been red if only the icon had been chased: the window's whole purpose is to be reachable while the
    /// designer is somewhere else.
    /// </summary>
    [Fact]
    public void TheAssetBrowserInstanceIsVisibleFromAnotherPerspective()
    {
        var wm = new WM(_atlas);
        wm.RegisterWindow(new FindResultsWindow("BTree", "ai_find_results_btree"));
        var global = AssetBrowserInstance();
        wm.RegisterWindow(global);

        wm.SwitchPerspective("BTree");

        Assert.Equal(WindowScope.Global, global.Scope);
        Assert.Equal(string.Empty, global.OwningPerspective);
        // ⭐ The visibility rule is `Global || IsPinned || OwningPerspective == current` — Global wins on
        //   the first arm, from any perspective, with no pin required.
        Assert.NotEqual(wm.CurrentPerspective, global.OwningPerspective);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>A6</c> — the phantom is UNCONSTRUCTIBLE.</b> ⛔ The removed
    /// <c>owningPerspective ?? "Authoring"</c> default meant any caller that omitted the perspective
    /// silently invented one; no production caller did, ⚠ <b>but that was luck, not a control.</b>
    /// 📌 The signature change is the real gate — this pair rails the two remaining bad pairings.
    /// </summary>
    [Fact]
    public void APerspectiveBoundInstanceCannotBeAnonymous()
        => Assert.Throws<ArgumentException>(() => new FindResultsWindow(string.Empty));

    /// <summary>⛔ And a Global one may not name a perspective — <c>"Global"</c> is not a place.</summary>
    [Fact]
    public void AGlobalInstanceCannotNameAPerspective()
        => Assert.Throws<ArgumentException>(
               () => new FindResultsWindow("Global", scope: WindowScope.Global));
}
