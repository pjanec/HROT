using System;
using System.IO;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.UiFrameRail;
using Raylib_cs;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Menus;

/// <summary>
/// ⭐ <c>CE-4114</c> — the bar's <c>Help</c> is the silk QUESTION MARK only (no "Help" text), flush right and last; <c>Windows</c> is under <c>View</c>; an unlisted
/// root segment sits after <c>Debug</c>. A REAL frame with the REAL silk atlas and the editor's own icon resolver (the bar's rules themselves are pinned in the
/// feature suites <c>TopLevelMenuOrderTests</c> / <c>TheMenuBarIsOneTreeTests</c>; this is the display rail they cannot be — they have no GL window).
/// ✅ Red-proof: <c>BarMenu.IconOnly</c> false ⇒ the draw label is "Help" ⇒ red; the <c>help</c> entry removed from <c>SilkIconProvider</c> ⇒ the resolver misses ⇒ red.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheHelpMenuIsAnIconTests
{
    private sealed class Blank : ManagedWindow
    {
        public Blank(string id, string perspective) : base(id, id, perspective, WindowScope.PerspectiveBound) { }
        protected override void DrawClientArea() { }
    }

    [SkippableFact]
    public void CE4114_TheBar_EndsWithTheQuestionMarkIcon_AndHasNoTopLevelWindows()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin();

        // the real silk atlas, loaded the way a host does (embedded PNG -> GL texture)
        var png = EmbeddedAtlasResources.GetSilkAtlasPngBytes();
        var img = Raylib.LoadImageFromMemory(".png", png);
        var tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        var wm = new WindowManager(new IconAtlas((nint)tex.Id, tex.Width, tex.Height, 16f));
        wm.MenuIcons = SilkMenuIconResolver.Create(wm.Atlas);
        wm.RegisterWindow(new Blank("w-IG", "IG"));
        wm.SwitchPerspective("IG");
        wm.GlobalMenu.RegisterItem("File/Open", () => { });
        wm.GlobalMenu.RegisterItem("Debug/Trace", () => { });
        wm.GlobalMenu.RegisterItem("Mission/Brief", () => { });   // an unlisted root segment

        var asked = new System.Collections.Generic.List<string>();
        var inner = wm.MenuIcons;
        wm.MenuIcons = (string key, out nint t, out System.Numerics.Vector2 a, out System.Numerics.Vector2 b) => { asked.Add(key); return inner(key, out t, out a, out b); };

        var bar = wm.GlobalMenu.BarMenus(wm.GlobalMenu.CollectContributions(wm.CurrentPerspective));
        Assert.Equal(new[] { "File", "View", "Debug", "Mission", "Help" }, bar.Select(m => m.Name));
        Assert.DoesNotContain(bar, m => m.Name == "Windows");
        var help = bar.Last();
        Assert.True(help.IconOnly);
        Assert.StartsWith("##", help.DrawLabel);   // no visible text
        Assert.True(inner(WindowManager.HelpIconKey, out var texId, out _, out _) && texId != 0, "the silk atlas resolves the Help icon key");

        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-menubar");
        frame.StepN(3, () => wm.Render());
        Assert.Contains(WindowManager.HelpIconKey, asked);   // the frame really drew the icon through the resolver
        frame.Screenshot(Path.Combine(shots, "ce4114-menubar.png"));
        Raylib.UnloadTexture(tex);
    }
}
