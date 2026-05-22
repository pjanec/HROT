using ImGuiNET;

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
        bool autoReload = _prefs.AutoReloadOnSave;
        if (ImGui.Checkbox("Auto Reload on Save", ref autoReload))
            _prefs.AutoReloadOnSave = autoReload;

        int logMax = _prefs.HotReloadLogMaxEntries;
        if (ImGui.InputInt("Hot Reload Log Max Entries", ref logMax))
            _prefs.HotReloadLogMaxEntries = System.Math.Max(1, logMax);

        ImGui.Separator();

        if (ImGui.Button("Save"))
            _prefs.Save(_savePath);

        ImGui.SameLine();

        if (ImGui.Button("Reset to Defaults"))
        {
            var defaults = BlueprintEditorPreferences.Defaults;
            _prefs.AutoReloadOnSave       = defaults.AutoReloadOnSave;
            _prefs.HotReloadLogMaxEntries = defaults.HotReloadLogMaxEntries;
        }
    }
}

