using Fdp.Presentation.WindowManager;
using Hrot.Editor.Windows;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ CE-1237 (🔒 User, `2026-10-21`: "the imgui panel showing blueprint attach button … is now shown in scenario perspective and has no close
/// button, i can not close it, coukd it become a normal panel with close box?") — Blueprint Tools was a raw <c>ImGui.Begin</c> with no open
/// flag, drawn in EVERY perspective; it is a managed window now: a close box (ManagedWindow passes its IsOpen to Begin), a View-menu entry,
/// and it belongs to the Blueprint perspective.
/// </summary>
public sealed class BlueprintToolsIsANormalPanelTests
{
    [Fact]
    public void CE1237_BlueprintTools_IsAManagedWindow_InTheBlueprintPerspective_AndTheRawWindowIsGone()
    {
        bool drawn = false;
        var window = new EditorBlueprintToolsWindow(() => drawn = true);
        Assert.IsAssignableFrom<ManagedWindow>(window);
        Assert.Equal(("Blueprint", WindowScope.PerspectiveBound), (window.OwningPerspective, window.Scope));
        Assert.True(window.IsOpen);
        window.IsOpen = false;   // what its close box does
        Assert.False(window.IsOpen);
        Assert.False(drawn);

        var editor = HostSource.Read("Hrot.Editor", "EditorSubsystem.cs");
        Assert.DoesNotContain("ImGui.Begin(\"Blueprint Tools\")", editor);
        Assert.Contains("RegisterWindow(new EditorBlueprintToolsWindow(DrawBlueprintToolsContent))", editor);
    }
}
