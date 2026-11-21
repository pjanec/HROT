using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐ CE-4099 (📄 AQ108 §4c A5 / A7, §4c.3) — the ONE place that names a scenario's autosave files and reads their state: the merge writes them
    /// (<c>StorageProcessManager</c>), the recovery load reads them (the prefetch), the HTTP route reports them. ⛔ No second copy of the names or the
    /// staleness rule.
    /// </summary>
    public static class AutosaveFiles
    {
        /// <summary>The scenario's autosave folder — a dot-folder, so no scenario enumerator lists it and CE-4091's copy skips it.</summary>
        public const string Folder = ".autosave";
        public const string DataFile = "autosave.json";
        public const string MetaFile = "autosave.meta.json";

        /// <summary>What a scenario's autosave is: none, or one taken at <see cref="AtUtc"/>; <see cref="Stale"/> when the <c>scenario.json</c> it was based on
        /// has changed since (saved elsewhere) — reported, never offered.</summary>
        public readonly record struct Status(bool Exists, DateTime AtUtc, bool Stale, string DataPath)
        {
            /// <summary>Why a recovery of it is refused (null = it may be recovered).</summary>
            public string? Refusal => !Exists ? "this scenario has no autosave" : Stale ? "the autosave is stale: the scenario was saved since it was taken" : null;
        }

        public static string DirectoryOf(string nasBasePath, string scenarioName)
            => Path.Combine(ContentLayout.ResolveScenarioDirectory(nasBasePath, scenarioName), Folder);

        /// <summary>Reads the autosave of <paramref name="scenarioName"/> under <paramref name="nasBasePath"/>. Staleness: the meta's <c>savedHash</c> against
        /// <paramref name="hashOf"/> of today's <c>scenario.json</c> (the hash the merge stamped).</summary>
        public static Status Read(string nasBasePath, string scenarioName, Func<string, string> hashOf)
        {
            if (hashOf == null) throw new ArgumentNullException(nameof(hashOf));
            var dir = DirectoryOf(nasBasePath, scenarioName);
            var data = Path.Combine(dir, DataFile);
            var metaPath = Path.Combine(dir, MetaFile);
            if (!File.Exists(data) || !File.Exists(metaPath)) return new Status(false, default, false, data);
            JsonNode? meta;
            try { meta = JsonNode.Parse(File.ReadAllText(metaPath)); }
            catch (System.Text.Json.JsonException) { return new Status(false, default, false, data); }
            DateTime at = DateTime.TryParse(meta?["at"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : default;
            string savedHash = meta?["savedHash"]?.GetValue<string>() ?? "";
            var saved = Path.Combine(ContentLayout.ResolveScenarioDirectory(nasBasePath, scenarioName), ContentLayout.ScenarioFileName);
            string current = File.Exists(saved) ? hashOf(File.ReadAllText(saved)) : "";
            return new Status(true, at, !string.Equals(savedHash, current, StringComparison.Ordinal), data);
        }
    }
}
