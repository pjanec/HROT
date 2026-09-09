using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using ImGuiNET;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 3) — this window's own state, dumped.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⛔ No id of its own — see <c>CallstackWindowPanelViewModel</c>'s remarks (same
/// <c>BlueprintEditorWindowBase</c> family). ⚠ <b>Measured</b> <c>2026-08-22</c>: this window is not
/// wired into <c>BlueprintWindowRegistrar</c> or any other production registration path — only its own
/// file and its tests reference it. Converted anyway per the full-sweep override ("every panel that
/// shows STATE"); the wiring gap is reported separately, not silently assumed away.</para>
/// </summary>
public sealed record PreferencesWindowPanelViewModel(
    string PanelId,
    string PanelKind,
    bool   AutoReloadOnSave,
    int    HotReloadLogMaxEntries) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

public sealed class PreferencesWindow : BlueprintEditorWindowBase
{
    /// <summary>⭐ <c>U-obs-5</c> — THE ADDRESS/KIND. ⛔ A declared literal — see
    /// <c>CallstackWindow.PanelId</c>'s remarks.</summary>
    internal const string PanelId = "preferences";

    private readonly BlueprintEditorPreferences _prefs;
    private readonly string _savePath;

    public override string Title => "Blueprint Preferences";

    public PreferencesWindow(BlueprintEditorPreferences prefs, string savePath)
    {
        _prefs    = prefs    ?? throw new ArgumentNullException(nameof(prefs));
        _savePath = savePath ?? throw new ArgumentNullException(nameof(savePath));

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(PanelId);
    }

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE. ⛔⛔ No ImGui.</summary>
    private PreferencesWindowPanelViewModel BuildAndPublish()
    {
        var vm = new PreferencesWindowPanelViewModel(
            PanelId, PanelId, _prefs.AutoReloadOnSave, _prefs.HotReloadLogMaxEntries);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal PreferencesWindowPanelViewModel SimulateDrawUI() => BuildAndPublish();

    public override void DrawUI()
    {
        BuildAndPublish();

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

