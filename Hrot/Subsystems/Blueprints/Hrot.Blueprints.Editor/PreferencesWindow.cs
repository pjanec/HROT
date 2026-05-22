namespace Hrot.Blueprints.Editor;

public sealed class PreferencesWindow : BlueprintEditorWindowBase
{
    private readonly BlueprintEditorPreferences _prefs;
    private readonly string _savePath;

    public override string Title => "Blueprint Preferences";

    public PreferencesWindow(BlueprintEditorPreferences prefs, string savePath)
    {
        _prefs    = prefs    ?? throw new ArgumentNullException(nameof(prefs));
        _savePath = savePath ?? throw new ArgumentNullException(nameof(savePath));
    }

    public override void DrawUI()
    {
        // ImGui form: AutoReloadOnSave checkbox, GraphEditorGridSnap slider, etc.
        // "Save" button: _prefs.Save(_savePath).
        // "Reset to Defaults" button: copy defaults into _prefs fields.
        // Requires ImGui runtime. Stub for Slice 1.
    }
}
