using System.Numerics;
using GizmoMap.Presentation;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Menus;

/// <summary>
/// ⭐ <c>CE-1027</c> (🔒 user, 2026-10-22: <i>"it was the windows menu, if hovered, its icon gone"</i>) — a submenu row's icon is drawn
/// on the ROW's window even while the submenu is OPEN. 📐 The defect: the renderers drew the icon AFTER <c>BeginMenu</c>, which — when the
/// menu is open — has already begun the submenu's popup, so the icon went into the popup's draw list at the row's position and was
/// clipped away (on the menu bar a menu opens when hovered while another is open). One helper, <see cref="MenuIconRenderer.BeginMenu"/>,
/// now serves all three renderers (the shell's menu tree, the DTO menus, the map's context menu).
/// ✅ Red-proof: drawing the icon after <c>ImGui.BeginMenu</c> onto <c>GetWindowDrawList()</c> (the old order) ⇒ the row gains 0 extra
/// vertices instead of the icon's 4.
/// </summary>
// ⭐ Real ImGui frames: never in parallel with another test that touches the ImGui context (a swapped context mid-frame made
//   the frame rails order-dependent — measured 2026-10-22, 1 red in 3 full runs).
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class AnOpenMenuKeepsItsIconTests
{
    [SkippableFact]
    public void CE1027_AnOpenSubmenu_DrawsItsIconOnTheRowsWindow()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(600, 400);
        int withIcon = -1, withoutIcon = -1;
        bool openedWith = false, openedWithout = false;

        // ⭐ A real, VALID texture (the font atlas), so the frame renders; the resolver hands it to the "folder" key only.
        MenuIconResolver icons = (string key, out nint tex, out Vector2 uv0, out Vector2 uv1) =>
        {
            tex = key == "folder" ? ImGui.GetIO().Fonts.TexID : 0;
            uv0 = Vector2.Zero; uv1 = Vector2.One;
            return tex != 0;
        };

        // ⭐ In a MENU BAR, as on the real bar (a menu bar does not close an open menu that is not hovered). ⛔ No assert inside the
        //   frame: a throw there skips End() and ImGui aborts the process.
        (int Delta, bool Open) OpenRowAndCountItsVertices(string label, string? key)
        {
            ImGui.SetNextWindowPos(new Vector2(200, 100));
            ImGui.SetNextWindowSize(new Vector2(300, 200));
            ImGui.Begin("menus##ce1027", ImGuiWindowFlags.MenuBar);
            int delta = 0; bool open = false;
            if (ImGui.BeginMenuBar())
            {
                var p0 = ImGui.GetCursorScreenPos();
                string padded = MenuIconRenderer.Pad(label, out float gutter);
                ImGui.OpenPopup(padded);                      // the menu is OPEN this frame, as when hovered on the bar
                var row = ImGui.GetWindowDrawList();
                int before = row.VtxBuffer.Size;
                open = MenuIconRenderer.BeginMenu(padded, true, icons, key, p0, gutter);
                delta = row.VtxBuffer.Size - before;
                if (open) { ImGui.CloseCurrentPopup(); ImGui.EndMenu(); }   // ⛔ leave no popup open in the shared harness window
                ImGui.EndMenuBar();
            }
            ImGui.End();
            return (delta, open);
        }

        frame.Step(() => (withIcon, openedWith)       = OpenRowAndCountItsVertices("Windows##a", "folder"));
        frame.Step(() => (withoutIcon, openedWithout) = OpenRowAndCountItsVertices("Windows##b", null));

        Assert.True(openedWith && openedWithout, "both submenus were open when their rows were drawn");
        Assert.Equal(4, withIcon - withoutIcon);   // one image quad, on the row's window
    }
}
