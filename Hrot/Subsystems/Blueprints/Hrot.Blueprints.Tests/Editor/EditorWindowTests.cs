using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Inspector;

namespace Hrot.Blueprints.Tests.Editor;

// BF-UX1 FIX D: GraphEditorWindow deleted (non-functional stub replaced by AiGraphCanvasWindow).
// Its three tests (Constructor_SetsTitle, SelectionChanged_OpensAsset, Constructor_ThrowsOnNullParams)
// have been removed along with the class.
public sealed class EditorWindowTests
{
    // ⛔ L5 — InspectorWindow_Constructor_SetsTitle DELETED with the stub it asserted.
    //    ⚠ It asserted a TITLE on a window whose body was placeholder text; nothing else covered it,
    //      and there is nothing left to cover.

    // PreferencesWindow

    [Fact]
    public void PreferencesWindow_Constructor_SetsTitle()
    {
        var w = new PreferencesWindow(BlueprintEditorPreferences.Defaults, "/tmp/prefs.json");
        Assert.Equal("Blueprint Preferences", w.Title);
    }

    [Fact]
    public void PreferencesWindow_Constructor_ThrowsOnNullParams()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PreferencesWindow(null!, "/tmp/prefs.json"));
        Assert.Throws<ArgumentNullException>(() =>
            new PreferencesWindow(BlueprintEditorPreferences.Defaults, null!));
    }
}
