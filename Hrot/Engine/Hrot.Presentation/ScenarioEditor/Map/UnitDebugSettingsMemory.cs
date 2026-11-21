using System;
using System.IO;
using System.Text.Json.Nodes;
using Fdp.Core;
using NodeEditor.Core.IO;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐ <c>CE-1329</c> (docs/DESIGN_Entity_Brain_View.md §12r BV-70) — remembers each scenario's per-unit debug settings
    /// (<see cref="UnitDebugSettings"/>: pins, traces, the followed unit) in a per-user file, <c>debug-settings/&lt;scenario&gt;.json</c> beside
    /// <c>map-layers.json</c>. Saved whenever they change (so a crash loses nothing) and when the scenario changes or the editor closes;
    /// loaded when a scenario loads. 🔒 User, 2026-10-25: <i>"save those on exit / scenario unload per scenario … restore automatically on
    /// scenario load — that survives editor crashes and restarts"</i>. Main thread only (the editor's update).
    /// </summary>
    public sealed class UnitDebugSettingsMemory
    {
        private readonly Func<string?> _directory;
        private string? _scenario;
        private int _savedVersion = -1;

        /// <param name="directory">The folder the files live in (null: remember nothing — a host with no user folder, a test).</param>
        public UnitDebugSettingsMemory(Func<string?> directory) => _directory = directory ?? throw new ArgumentNullException(nameof(directory));

        /// <summary>The default folder: <c>&lt;user folder&gt;/debug-settings</c>, or null when there is no user folder.</summary>
        public static string? DefaultDirectory()
        {
            return Hrot.UI.Common.UserFiles.TryPathOf("debug-settings");
        }

        /// <summary>The file of <paramref name="scenario"/> (its name made file-safe), or null without a folder.</summary>
        public string? PathOf(string scenario)
        {
            if (_directory() is not { Length: > 0 } dir) return null;
            var safe = string.Join("_", scenario.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            return Path.Combine(dir, (safe.Length == 0 ? "unnamed" : safe) + ".json");
        }

        /// <summary>Once a frame: a new scenario ⇒ save the old one's and load the new one's; a change ⇒ save.</summary>
        public void Tick(EntityRepository world, string? scenario)
        {
            var store = UnitDebugSettings.Of(world);
            if (!string.Equals(scenario, _scenario, StringComparison.Ordinal))
            {
                Save(store);
                _scenario = scenario;
                store.Load(scenario is { Length: > 0 } s && PathOf(s) is { } path && File.Exists(path) ? Read(path) : null);
                _savedVersion = store.Version;
                return;
            }
            if (store.Version != _savedVersion) Save(store);
        }

        /// <summary>Writes the current scenario's settings now (on exit).</summary>
        public void Save(UnitDebugSettings store)
        {
            _savedVersion = store.Version;
            if (_scenario is not { Length: > 0 } s || PathOf(s) is not { } path) return;
            try
            {
                // a writer, not JsonSerializerOptions: this repo turns reflection-based serialization off (measured: the options form threw
                // "must specify a TypeInfoResolver", swallowed below — nothing was ever saved)
                using var bytes = new MemoryStream();
                using (var writer = new System.Text.Json.Utf8JsonWriter(bytes, new System.Text.Json.JsonWriterOptions { Indented = true }))
                    store.ToJson().WriteTo(writer);
                SafeTextFile.TryWrite(path, System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
            }
            catch (Exception) { /* a read-only folder: the settings live for this run only */ }
        }

        private static JsonNode? Read(string path)
        {
            try { return SafeTextFile.TryRead(path) is { } text ? JsonNode.Parse(text) : null; }
            catch (Exception) { return null; }   // a bad file: start clean
        }
    }
}
