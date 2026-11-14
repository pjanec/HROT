using System;
using System.Collections.Generic;
using Fdp.Presentation.Editing;
using Hrot.Editor.AiShared.Blackboard;
using System.Numerics;
using Fdp.Toolkit.Scenario;
using Hrot.UI.Common.RunMode;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.Editor.Scenario;

/// <summary>
/// ⭐ CE-4210 P2 (MA-5) → ⭐ CE-4253 → ⭐ N-6 (CE-3383) — the scenario / macro's PARAMETERS editor: the ONE Define table (<see cref="FieldTable"/>) over the declared params
/// (<see cref="HeaderParamsSchemaSource"/> through <see cref="SchemaSourceDeclarations"/>); every edit — a rename moves the declaration and every binding in ONE step — is the source's own, with its ONE undo history (the shell's Edit ▸ Undo / Redo).
/// ⭐ CE-4253 — this body is a DRAWER the Details ▸ Scenario view calls inside its collapsible <i>Parameters</i> header (⭐ CE-4346: now <see cref="DrawHeader"/>, under the ONE Scenario tab — the form).
/// </summary>
/// <summary>What the loaded map holds, for the <i>"The map holds: …"</i> line: the preset whose args the world was loaded with, whether the world can be edited and saved, and why not.</summary>
public readonly record struct MapHolding(string PresetName, bool Editable, string? Reason);

public sealed class ParametersDrawer
{
    private readonly HeaderParamsSchemaSource _source;
    private readonly SchemaSourceDeclarations _decls;
    private readonly PresetDeclarations _view;
    private readonly FieldTable _table = new();
    private readonly FieldTableSettings _defaultSettings;
    private readonly IReadOnlyList<TypeChoice> _choices;
    private FieldTableSettings? _presetSettings;
    private string? _presetSettingsFor;

    // ⭐ CE-3391 — the host's three seams (the silent-default rule: EditorSubsystem passes all of them; a null one simply hides its line / greys its button)
    /// <summary>What the map holds (the loaded preset, editable?, why not) — read at most twice a second.</summary>
    /// <summary>
    /// ⭐ CE-4253 / CE-4346 — the scenario's PARAMETERS as a collapsible header, CLOSED by default (🔒 user: <i>"a collapsible header, by default collapsed, called
    /// parameters … the same editor that now opens in a separate panel"</i>), drawn under the ONE Scenario tab (the form). Opened for a frame when something
    /// asked (<see cref="RequestOpen"/>). ⛔ SUPERSEDED: it lived in the retired raw Scenario view (<c>ScenarioEnvironmentView</c>).
    /// </summary>
    public void DrawHeader()
    {
        ImGuiNET.ImGui.Separator();
        if (ConsumeOpenRequest()) ImGuiNET.ImGui.SetNextItemOpen(true, ImGuiNET.ImGuiCond.Always);
        HeaderOpen = NodeEditor.Core.Widgets.SectionHeader.Draw("hdr.parameters", "Parameters", "section/params");
        if (HeaderOpen) Draw();
    }

    /// <summary>For rails: whether the Parameters header was open on the last draw.</summary>
    internal bool HeaderOpen { get; private set; }

    public Func<MapHolding?>? MapHolds { get; set; }
    /// <summary>The <i>Preview this preset</i> button: the host arms Preview with the preset and plays.</summary>
    public Action<string>? PreviewPreset { get; set; }
    /// <summary>True when the scenario uses variants (the Variant row is only drawn then).</summary>
    public Func<bool>? UsesVariants { get; set; }
    /// <summary>⭐ CE-3402 (Y-8) — the pick context of the table's value cells (a link-type default — EntityLink / PointLink / AreaLink — gets <i>Pick on the map</i> and ⌖ show-on-map; Field Declaration §6 D9). The host passes its ONE broker; null = no map affordance.</summary>
    public Func<Fdp.Presentation.Editing.IMapPickContext?>? Picks { get; set; }

    private string _shown = ScenarioPreset.DefaultName;
    private int _seenRevision = -1;
    private ScenarioPreset[] _all = Array.Empty<ScenarioPreset>();
    private string[] _labels = Array.Empty<string>(), _summaries = Array.Empty<string>();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private long _hostReadAt = -1000;
    private MapHolding? _holds;
    private bool _usesVariants;
    private int _variantN = 1;
    private string? _variantNFor;
    private readonly TextBuffer _renameBuf = new(64);
    private string? _presetError;
    private bool _openRename;

    // probes (rails)
    internal (Vector2 Min, Vector2 Max) ComboRect, NewRect, MenuRect, PreviewRect;
    /// <summary>The preset the table shows now.</summary>
    public string Shown => _shown;
    /// <summary>The line under the combo / the last preset refusal.</summary>
    internal string? PresetError => _presetError;
    internal bool VariantRowDrawn { get; private set; }

    /// <summary>⭐ N-6 — the table (frame rails read its probes).</summary>
    internal FieldTable Table => _table;
    internal HeaderParamsSchemaSource Source => _source;

    /// <summary>Why the last rename was refused (CE-4254): the reason under the row while its rename box is still open; null otherwise.</summary>
    public string? RenameRefusal => _table.RenamingName != null && _table.Error is { } e ? e.Text : null;

    /// <summary>Frame-rail probe (CE-4254): where the name cells were drawn.</summary>
    public IReadOnlyList<(string Name, System.Numerics.Vector2 Min, System.Numerics.Vector2 Max)> NameCells => _table.NameCells;

    /// <param name="editService">Kept for the call sites: the value cells draw from the type catalogue now, not from a StructEdit service.</param>
    public ParametersDrawer(HeaderParamsSchemaSource source, Func<StructEdit.Core.IComponentEditService?>? editService = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _decls = new SchemaSourceDeclarations(source, id => Fdp.Toolkit.Scenario.ParamTypes.TryResolve(id, out var t) ? t : null);
        _view = new PresetDeclarations(_decls, source, () => _holds is { Editable: false });
        var choices = new List<TypeChoice>();
        foreach (var (id, type) in Fdp.Toolkit.Scenario.ParamTypes.All()) choices.Add(new TypeChoice(id, type, GroupOf(type)));
        _choices = choices;
        _defaultSettings = Settings("Default", showDefaults: false);
    }

    private FieldTableSettings Settings(string valueHeader, bool showDefaults) => new()
    {
        AddLabel = "Add parameter", NewNameStem = "param", ValueHeader = valueHeader, UsedBy = true, ShowDefaults = showDefaults,
        AllowStructs = false, AllowLists = false,
        StructsOffReason = "structures and lists are off for scenario parameters", ListsOffReason = "structures and lists are off for scenario parameters",
        TypeChoices = _choices,
    };

    /// <summary>The picker's group of a parameter type: enums · the links and the bearing / rotation (references and places) · plain values.</summary>
    private static TypeGroup GroupOf(Type t)
        => t.IsEnum ? TypeGroup.Enums
         : t == typeof(Fdp.Toolkit.Replication.EntityLink) || t == typeof(Fdp.Toolkit.Replication.PointLink) || t == typeof(Fdp.Toolkit.Replication.AreaLink)
           || t == typeof(Fdp.Core.Bearing) || t == typeof(Fdp.Core.WorldRotation) ? TypeGroup.References
         : FieldTableSettings.GroupOf(t);

    /// <summary>For rails: how many times the body was drawn (the header closed ⇒ not at all).</summary>
    internal int DrawCount { get; private set; }

    private bool _openRequested;

    /// <summary>⭐ CE-4253 — asks the Scenario view to show its Parameters header EXPANDED the next time it draws (the bind popup's <i>New parameter…</i> calls this).</summary>
    public void RequestOpen() => _openRequested = true;

    /// <summary>⭐ True once per <see cref="RequestOpen"/> — the view expands the header for that frame.</summary>
    public bool ConsumeOpenRequest() { bool r = _openRequested; _openRequested = false; return r; }

    /// <summary>Shows <paramref name="preset"/> in the table (Default ⇒ the declared values). ⛔ Never loads anything (D2).</summary>
    public void Show(string preset)
    {
        _shown = string.IsNullOrWhiteSpace(preset) ? ScenarioPreset.DefaultName : preset;
        _view.Shown = _shown;
        _presetError = null;
    }

    private bool ShownIsDefault => string.Equals(_shown, ScenarioPreset.DefaultName, StringComparison.OrdinalIgnoreCase);

    private ScenarioPreset ShownPreset()
    {
        foreach (var p in _all) if (string.Equals(p.Name, _shown, StringComparison.OrdinalIgnoreCase)) return p;
        return _all.Length > 0 ? _all[0] : new ScenarioPreset(ScenarioPreset.DefaultName, PresetVariant.AsAuthored);
    }

    /// <summary>Re-reads the preset list when the header moved (Default first), drops a shown preset that is gone (an undo, a load).</summary>
    private void Refresh()
    {
        int rev = _source.Revision;
        if (rev == _seenRevision) return;
        _seenRevision = rev;
        var stored = _source.Presets();
        _all = new ScenarioPreset[stored.Count + 1];
        _all[0] = new ScenarioPreset(ScenarioPreset.DefaultName, PresetVariant.AsAuthored);
        for (int i = 0; i < stored.Count; i++) _all[i + 1] = stored[i];
        _labels = new string[_all.Length]; _summaries = new string[_all.Length];
        for (int i = 0; i < _all.Length; i++) { _labels[i] = PresetText.Label(_all[i]); _summaries[i] = i == 0 ? "declared defaults" : PresetText.Summary(_all[i]); }
        bool found = false;
        foreach (var p in _all) if (string.Equals(p.Name, _shown, StringComparison.OrdinalIgnoreCase)) found = true;
        if (!found) Show(ScenarioPreset.DefaultName);
    }

    private void ReadHost()
    {
        long now = _clock.ElapsedMilliseconds;
        if (now - _hostReadAt < 500) return;
        _hostReadAt = now;
        _holds = MapHolds?.Invoke();
        _usesVariants = UsesVariants?.Invoke() ?? false;
    }

    /// <summary>The drawer's content: what the map holds, the preset row, the variant row, the table (its refusals are drawn under the row they belong to).</summary>
    public void Draw()
    {
        DrawCount++;
        ImGui.Separator();
        if (_source.IsReadOnly) { ImGui.TextDisabled("No scenario is loaded."); return; }
        Refresh();
        ReadHost();
        DrawMapHolds();
        bool ro = _holds is { Editable: false };
        DrawPresetRow(ro);
        ImGui.BeginDisabled(ro);
        DrawVariantRow();
        ImGui.EndDisabled();
        if (_presetError is { } err) RowMarks.ErrorLine(err);
        ImGui.Spacing();
        FieldTableSettings settings = _defaultSettings;
        if (!ShownIsDefault)
        {
            if (_presetSettingsFor != _shown) { _presetSettingsFor = _shown; _presetSettings = Settings("Value · " + _shown, showDefaults: true); }
            settings = _presetSettings!;
        }
        _table.Draw(_view, settings, Picks?.Invoke());
        // ⛔ B-14 — Ctrl+Z / Ctrl+Y are the shell's Edit ▸ Undo / Redo now (the ONE key mechanism over the SAME history): the drawer's own chord would undo twice.
    }

    // ── "The map holds: …" ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    private void DrawMapHolds()
    {
        if (_holds is not { } h) return;
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, h.Editable ? IconPalette.Amber : IconPalette.Violet);
        ImGui.TextUnformatted(h.Editable ? Fdp.Presentation.Fonts.IconsFontAwesome6.Pen : Fdp.Presentation.Fonts.IconsFontAwesome6.Eye);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.TextDisabled("The map holds:");
        ImGui.SameLine();
        ImGui.TextUnformatted(h.PresetName);
        ImGui.SameLine();
        ImGui.TextDisabled(h.Editable ? "— editable, Save on" : "— preview only, Save off" + (string.IsNullOrEmpty(h.Reason) ? "" : " (" + h.Reason + ")"));
    }

    // ── the preset row: combo · [+] · [⋮] · Preview this preset ───────────────────────────────────────────────────────────────────

    private void DrawPresetRow(bool ro)
    {
        float h = ImGui.GetFrameHeight();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Preset");
        ImGui.SameLine(64f);
        int shownIdx = 0;
        for (int i = 0; i < _all.Length; i++) if (string.Equals(_all[i].Name, _shown, StringComparison.OrdinalIgnoreCase)) shownIdx = i;
        ImGui.SetNextItemWidth(Math.Max(120f, ImGui.GetContentRegionAvail().X * 0.45f));
        bool open = ImGui.BeginCombo("##preset", _labels[shownIdx], ImGuiComboFlags.HeightLarge);
        ComboRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (open)
        {
            for (int i = 0; i < _all.Length; i++)
            {
                bool sel = i == shownIdx;
                if (ImGui.Selectable(_labels[i], sel)) Show(_all[i].Name);   // picks what the TABLE shows; the host's load is never called
                ImGui.Indent(18f); ImGui.TextDisabled(_summaries[i]); ImGui.Unindent(18f);
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(ro);   // an eye world cannot be saved: its presets are not edited (D5); showing one is harmless
        if (GlyphWidgets.GlyphButton("##newpreset", new IconGlyph(KitGlyph.Plus, IconPalette.Green), "New preset — a copy of the one shown")) NewPreset();
        NewRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine();
        if (GlyphWidgets.GlyphButton("##presetmenu", new IconGlyph(KitGlyph.Ellipsis, IconPalette.Grey), "Rename · Duplicate · Delete · Move · Reset")) ImGui.OpenPopup("##presetmenupop");
        MenuRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        DrawPresetMenu(shownIdx);
        ImGui.EndDisabled();
        ImGui.SameLine(0f, 16f);
        bool canPreview = PreviewPreset is not null;
        if (!canPreview) ImGui.BeginDisabled();
        ImGui.PushStyleColor(ImGuiCol.Text, IconPalette.Violet);
        string glyph = Fdp.Presentation.Fonts.IconsFontAwesome6.Eye;
        ImGui.TextUnformatted(glyph);
        ImGui.PopStyleColor();
        ImGui.SameLine(0f, 4f);
        if (ImGui.Button("Preview this preset")) PreviewPreset!(_shown);
        PreviewRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (!canPreview) ImGui.EndDisabled();
        ImGui.Indent(64f); ImGui.TextDisabled(_summaries[shownIdx]); ImGui.Unindent(64f);
    }

    private void DrawPresetMenu(int shownIdx)
    {
        if (!ImGui.BeginPopup("##presetmenupop")) return;
        bool isDefault = ShownIsDefault;
        const string DefaultWhy = "Default is the declared values — it has no name to change and cannot be deleted";
        if (ImGui.MenuItem("Rename…", null, false, !isDefault)) { _renameBuf.Set(_shown); _openRename = true; }
        Why(isDefault, DefaultWhy);
        if (ImGui.MenuItem("Duplicate")) { var made = isDefault ? _source.AddPresetCopyOf(_shown) : _source.DuplicatePreset(_shown); AfterPresetEdit(made); }
        if (ImGui.MenuItem("Delete", null, false, !isDefault)) { if (_source.DeletePreset(_shown)) Show(ScenarioPreset.DefaultName); AfterPresetEdit(null); }
        Why(isDefault, DefaultWhy);
        ImGui.Separator();
        if (ImGui.MenuItem("Move up", null, false, !isDefault && shownIdx > 1)) { _source.MovePreset(_shown, -1); AfterPresetEdit(null); }
        if (ImGui.MenuItem("Move down", null, false, !isDefault && shownIdx < _all.Length - 1)) { _source.MovePreset(_shown, 1); AfterPresetEdit(null); }
        Why(isDefault, "Default is always first");
        ImGui.Separator();
        if (ImGui.MenuItem("Reset all to default", null, false, !isDefault && ShownPreset().Args is { Count: > 0 })) { _source.ResetPreset(_shown); AfterPresetEdit(null); }
        Why(isDefault, "Default already is the declared values");
        ImGui.EndPopup();
        if (_openRename) { _openRename = false; ImGui.OpenPopup("##presetrename"); }
    }

    private static void Why(bool disabled, string reason)
    {
        if (disabled && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(reason);
    }

    private void AfterPresetEdit(string? shown)
    {
        _presetError = _source.LastRefusal;
        if (shown != null) Show(shown);
        _seenRevision = -1;
    }

    /// <summary>[+] — a new preset that is a copy of the one shown ("Preset N", unique); it becomes the shown one.</summary>
    internal void NewPreset() => AfterPresetEdit(_source.AddPresetCopyOf(_shown));

    /// <summary>The rename popup's OK.</summary>
    internal bool RenameShown(string newName)
    {
        bool ok = _source.RenamePreset(_shown, newName);
        if (ok) Show(newName.Trim());
        _presetError = ok ? null : _source.LastRefusal;
        _seenRevision = -1;
        return ok;
    }

    // ── the variant row (only when the scenario uses variants) ───────────────────────────────────────────────────────────────────

    private void DrawVariantRow()
    {
        VariantRowDrawn = false;
        DrawRenamePopup();
        if (!_usesVariants || ShownIsDefault) return;
        VariantRowDrawn = true;
        var p = ShownPreset();
        var kind = p.Variant.Kind;
        if (_variantNFor != _shown) { _variantNFor = _shown; _variantN = p.Variant.Number != 0 ? p.Variant.Number : 1; }
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Variant");
        ImGui.SameLine(64f);
        if (ImGui.RadioButton("As authored", kind == PresetVariantKind.AsAuthored) && kind != PresetVariantKind.AsAuthored) { _source.SetPresetVariant(_shown, PresetVariant.AsAuthored); _seenRevision = -1; }
        ImGui.SameLine(0f, 24f);
        if (ImGui.RadioButton("Random each run", kind == PresetVariantKind.Random) && kind != PresetVariantKind.Random) { _source.SetPresetVariant(_shown, PresetVariant.RandomEachRun); _seenRevision = -1; }
        ImGui.SameLine(0f, 24f);
        if (ImGui.RadioButton("Number", kind == PresetVariantKind.Number) && kind != PresetVariantKind.Number) { _source.SetPresetVariant(_shown, new PresetVariant(PresetVariantKind.Number, Math.Max(1, _variantN))); _seenRevision = -1; }
        ImGui.SameLine();
        ImGui.BeginDisabled(kind != PresetVariantKind.Number);
        ImGui.SetNextItemWidth(70f);
        ImGui.InputInt("##variantn", ref _variantN, 0, 0);
        if (ImGui.IsItemDeactivatedAfterEdit() && kind == PresetVariantKind.Number && _variantN != p.Variant.Number) { _source.SetPresetVariant(_shown, new PresetVariant(PresetVariantKind.Number, _variantN)); _seenRevision = -1; }
        ImGui.EndDisabled();
        if (!p.Variant.Saveable)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, IconPalette.Violet);
            ImGui.TextUnformatted(Fdp.Presentation.Fonts.IconsFontAwesome6.Eye + "  Preview only — a world built with a variant can't be saved (some entities may be missing).");
            ImGui.PopStyleColor();
        }
    }

    private void DrawRenamePopup()
    {
        if (!ImGui.BeginPopup("##presetrename")) return;
        ImGui.TextDisabled("Rename preset");
        _renameBuf.Draw("##presetname", "Name", 220f);
        if (_presetError is { } err) RowMarks.ErrorLine(err);
        if (ImGui.Button("Rename")) { if (RenameShown(_renameBuf.Text)) ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}
