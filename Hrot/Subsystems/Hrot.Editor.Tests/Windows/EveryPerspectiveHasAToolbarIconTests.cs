using System;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.Interfaces;
using Xunit;

using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-058</c> — every perspective a windowed host claims resolves a TOOLBAR ICON.</b>
/// 📄 The user's <c>--mode all</c> check, <c>2026-08-27</c>: *"instead of graphical icons (as rendered in
/// the editor) there are plain imgui buttons in the toolbar"*.
///
/// <para>⭐⭐ <b>The property this rail pins, and why it is the RIGHT one.</b>
/// <see cref="PerspectiveToolbarSection.BuildRadioModel"/> sets <c>HasIcon</c> from
/// <c>GetPerspectiveIconKey(p) != null &amp;&amp; provider.TryGet(key)</c>, and the render path falls back
/// to a <b>text-label button</b> when it is false. ⇒ the plain buttons were not a rival toolbar
/// implementation — ⛔ both hosts build the same section over the same provider — they were this
/// documented fallback firing because <c>RegisterPerspectiveIconKey</c> had exactly ONE caller repo-wide.
/// ⭐ So the checkable claim is <i>"the key resolves"</i>, ⛔ not <i>"a section was constructed"</i>
/// (which <c>CE-054</c> already asserted, truthfully, while the faces were still text).</para>
///
/// <para>⚠ <b>Deliberately NOT a source scan.</b> A grep for the <c>Register…</c> call would pass for a
/// host that registers keys for perspectives it does not claim and misses one it does. ⭐ This asks the
/// real <c>WindowManager</c> after the real <c>RegisterWindows</c>, so a host that gains a perspective
/// without an icon reddens here.</para>
/// </summary>
public sealed class EveryPerspectiveHasAToolbarIconTests
{
    private static WM Register(Action<WM> registerWindows)
    {
        var wm = new WM(new IconAtlas(IntPtr.Zero, 16f, 16f));
        registerWindows(wm);
        return wm;
    }

    /// <summary>
    /// ⭐⭐⭐ The gate. ⚠ Named per host so a failure says WHICH host lost its icons.
    /// </summary>
    [Theory]
    [InlineData("editor")]
    [InlineData("cgf")]
    public void EveryClaimedPerspectiveResolvesAnIconKey(string host)
    {
        var wm = host == "editor"
            ? Register(w => new Hrot.Editor.EditorSubsystem().RegisterWindows(w))
            : Register(w => new Hrot.CGF.CgfSubsystem().RegisterWindows(w));

        var perspectives = wm.GetPerspectives();
        Assert.NotEmpty(perspectives);   // ⛔ else the rail passes vacuously

        foreach (var p in perspectives)
            Assert.False(string.IsNullOrEmpty(wm.GetPerspectiveIconKey(p)),
                $"perspective '{p}' on {host} resolves NO icon key ⇒ PerspectiveToolbarSection renders a "
              + "plain text button for it. Add it to PerspectiveIconKeys.Table.");
    }

    /// <summary>
    /// ⭐⭐ <b>The two hosts resolve the SAME key for a shared perspective.</b> 📐 <c>Scenario</c> is the
    /// one they share (<c>ThePerspectiveNamesAreUnifiedTests</c> measures that), so a per-host key here
    /// would mean one concept with two faces.
    /// </summary>
    [Fact]
    public void BothHostsResolveTheSameKeyForScenario()
    {
        var editor = Register(w => new Hrot.Editor.EditorSubsystem().RegisterWindows(w));
        var cgf    = Register(w => new Hrot.CGF.CgfSubsystem().RegisterWindows(w));

        Assert.Equal(editor.GetPerspectiveIconKey("Scenario"), cgf.GetPerspectiveIconKey("Scenario"));
        Assert.False(string.IsNullOrEmpty(cgf.GetPerspectiveIconKey("Scenario")));
    }

    /// <summary>
    /// ⭐⭐ <b><see cref="PerspectiveToolbarSection.BuildRadioModel"/> reports <c>HasIcon</c> for every
    /// entry</b> — the model the render path actually consumes, over a provider that resolves the shared
    /// table's keys. ⚠ This is the assertion closest to the pixel; the two above are its inputs.
    /// </summary>
    [Fact]
    public void TheRadioModelHasAnIconForEveryCgfEntry()
    {
        var wm = Register(w => new Hrot.CGF.CgfSubsystem().RegisterWindows(w));
        Assert.NotNull(wm.MainToolbar);

        var section = new PerspectiveToolbarSection(
            wm, new TableIconProvider(), wm.MainToolbar!, sortOrder: 20);

        var model = section.BuildRadioModel();
        Assert.NotEmpty(model);
        Assert.All(model, e => Assert.True(e.HasIcon,
            $"'{e.Perspective}' would render as a TEXT BUTTON — the CE-058 symptom."));
    }

    /// <summary>
    /// ⛔⛔ <b>The inverse, so none of the above can pass vacuously:</b> a WindowManager whose windows are
    /// registered but whose keys are NOT gives <c>HasIcon == false</c> — i.e. exactly the state CGF
    /// shipped in. ⚠ Without this rail, an <c>IIconProvider</c> that resolved everything would make the
    /// gate green regardless of the registration.
    /// </summary>
    [Fact]
    public void WithoutTheKeysTheRadioModelHasNoIcons()
    {
        // ⭐ A bare manager with one perspective-bound window and NO PerspectiveIconKeys.Register call.
        var wm = new WM(new IconAtlas(IntPtr.Zero, 16f, 16f));
        wm.RegisterWindow(new BareScenarioWindow());

        Assert.Contains("Scenario", wm.GetPerspectives());
        Assert.Null(wm.GetPerspectiveIconKey("Scenario"));

        var section = new PerspectiveToolbarSection(
            wm, new TableIconProvider(), wm.MainToolbar!, sortOrder: 20);

        Assert.All(section.BuildRadioModel(), e => Assert.False(e.HasIcon));
    }

    /// <summary>⭐ Resolves exactly the keys the shared table declares — ⛔ nothing else, so a host that
    /// invents a key outside the table reddens rather than passing on a permissive stub.</summary>
    private sealed class TableIconProvider : IIconProvider
    {
        public bool TryGet(string key, out IconHandle handle)
        {
            handle = default;
            return PerspectiveIconKeys.Table.Any(t => t.IconKey == key);
        }
    }

    /// <summary>⭐ A minimal perspective-bound window with NO IconKey, so the window-scan fallback in
    /// <c>GetPerspectiveIconKey</c> cannot rescue the inverse rail.</summary>
    private sealed class BareScenarioWindow : ManagedWindow
    {
        public BareScenarioWindow()
            : base("bare_scenario", "Bare", "Scenario", WindowScope.PerspectiveBound) { }

        protected override void DrawClientArea() { }
    }
}
