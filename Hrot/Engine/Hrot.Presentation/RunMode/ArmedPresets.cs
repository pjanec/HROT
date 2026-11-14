using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Scenario;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐ CE-3393 / presets D11 (📄 docs/UX/DESIGN_Scenario_Presets.md §5c, D11) — the preset ARMED per scenario and per mode (Preview · Live), remembered in the USER's folder
/// (<c>armed-presets.json</c>, beside <c>recent-picks.json</c> — a person's choice, never the scenario's). ⭐ <see cref="Shared"/> is the host's; a store with no path is in memory (a rail).
/// ⛔ No allocation on a read: keyed by (scenario, mode).
/// </summary>
public sealed class ArmedPresets
{
    /// <summary>The user-folder file name.</summary>
    public const string FileName = "armed-presets.json";

    private static readonly Lazy<ArmedPresets> SharedStore = new(() => Open(UserFiles.TryPathOf(FileName)), isThreadSafe: true);

    /// <summary>The host's store (the user folder; in memory when there is none).</summary>
    public static ArmedPresets Shared => SharedStore.Value;

    private readonly Dictionary<(string Scenario, RunArmed Mode), string> _armed = new();
    private readonly object _gate = new();

    private ArmedPresets(string? path) => PersistPath = string.IsNullOrWhiteSpace(path) ? null : path;

    /// <summary>Where it is saved; null = in memory.</summary>
    public string? PersistPath { get; }

    /// <summary>A store over <paramref name="path"/> (null / empty = in memory), loaded from it when the file exists.</summary>
    public static ArmedPresets Open(string? path)
    {
        var store = new ArmedPresets(path);
        if (store.PersistPath is { } p && File.Exists(p))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(p)) is JsonObject o)
                    foreach (var (key, v) in o)
                    {
                        int bar = key.LastIndexOf('|');
                        if (bar <= 0 || v is not JsonValue jv || !jv.TryGetValue<string>(out var name)) continue;
                        if (Enum.TryParse<RunArmed>(key[(bar + 1)..], out var mode)) store._armed[(key[..bar], mode)] = name;
                    }
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }   // unreadable ⇒ nothing armed
        }
        return store;
    }

    /// <summary>The preset name armed for <paramref name="scenario"/> in <paramref name="mode"/>; <see cref="ScenarioPreset.DefaultName"/> when none.</summary>
    public string Armed(string scenario, RunArmed mode)
    {
        lock (_gate) return _armed.TryGetValue((scenario, mode), out var n) ? n : ScenarioPreset.DefaultName;
    }

    /// <summary>Arms <paramref name="preset"/> for <paramref name="scenario"/> in <paramref name="mode"/> and saves (Default = forget).</summary>
    public void Arm(string scenario, RunArmed mode, string preset)
    {
        lock (_gate)
        {
            if (string.Equals(preset, ScenarioPreset.DefaultName, StringComparison.OrdinalIgnoreCase)) _armed.Remove((scenario, mode));
            else _armed[(scenario, mode)] = preset;
            Save();
        }
    }

    private void Save()
    {
        if (PersistPath is not { } p) return;
        try
        {
            var o = new JsonObject();
            foreach (var kv in _armed) o[kv.Key.Scenario + "|" + kv.Key.Mode] = kv.Value;
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, o.ToJsonString());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // a read-only folder: it stays armed for this session
    }
}

/// <summary>⭐ CE-3392 / CE-3393 — how a preset reads in a list (the load dialog's combo, the ▶ ▾ menu): ONE wording.</summary>
public static class PresetText
{
    /// <summary>The glyph (pen: as authored, editable · eye: a variant, preview only) and the name.</summary>
    public static string Label(ScenarioPreset p)
        => (p.Variant.Saveable ? Fdp.Presentation.Fonts.IconsFontAwesome6.Pen : Fdp.Presentation.Fonts.IconsFontAwesome6.Eye) + "  " + p.Name;

    /// <summary>The one-line summary (presets §5a): <i>as authored · 2 changed</i> · <i>variant 3 · preview only</i> · <i>random each run · preview only</i>.</summary>
    public static string Summary(ScenarioPreset p)
    {
        string variant = p.Variant.Kind switch
        {
            PresetVariantKind.Random => "random each run",
            PresetVariantKind.Number when p.Variant.Number != 0 => "variant " + p.Variant.Number,
            _ => "as authored",
        };
        int changed = p.Args?.Count ?? 0;
        return variant + (changed == 0 ? "" : " · " + changed + " changed") + (p.Variant.Saveable ? "" : " · preview only");
    }

    /// <summary>
    /// True when the world's load args (<paramref name="loadedArgsJson"/>, <c>ScenarioDescription.ArgsOf</c>) are what <paramref name="preset"/> loads: the same overrides, and a variant
    /// that fits (as authored: none or 0 · a number: that one · random: any non-zero). Default = no overrides, as authored.
    /// </summary>
    public static bool IsLoaded(ScenarioPreset preset, string? loadedArgsJson)
    {
        JsonObject loaded;
        try { loaded = string.IsNullOrWhiteSpace(loadedArgsJson) ? new JsonObject() : JsonNode.Parse(loadedArgsJson) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return false; }
        int variant = loaded[ScenarioVariants.ArgName] is JsonValue vv && vv.TryGetValue<int>(out var v) ? v : 0;
        loaded.Remove(ScenarioVariants.ArgName);
        bool variantFits = preset.Variant.Kind switch
        {
            PresetVariantKind.Random => variant != 0,
            PresetVariantKind.Number => variant == preset.Variant.Number,
            _ => variant == 0,
        };
        var wanted = preset.Args ?? new JsonObject();
        return variantFits && JsonNode.DeepEquals(loaded, wanted);
    }
}
