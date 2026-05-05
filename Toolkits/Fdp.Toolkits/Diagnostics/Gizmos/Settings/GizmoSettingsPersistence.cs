using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Settings
{
    /// <summary>Saves and loads gizmo setting overrides to/from a JSON file.</summary>
    public static class GizmoSettingsPersistence
    {
        /// <summary>
        /// Writes only settings whose active value differs from the registered default.
        /// Clears <see cref="GizmoSettingsRegistry.IsDirty"/> on success.
        /// </summary>
        public static void SaveOverrides(GizmoSettingsRegistry registry, string filePath)
        {
            var records = new List<SettingRecord>();

            foreach (var (key, active, def) in registry.EnumerateAll())
            {
                if (active == def) continue;

                records.Add(new SettingRecord
                {
                    key   = key,
                    type  = active.Type.ToString(),
                    value = FormatValue(active),
                });
            }

            string json = JsonSerializer.Serialize(records);
            File.WriteAllText(filePath, json);
            registry.ClearDirty();
        }

        /// <summary>
        /// Reads overrides from <paramref name="filePath"/> and applies them via
        /// <see cref="GizmoSettingsRegistry.Write"/>.
        /// Returns silently when the file does not exist.
        /// </summary>
        public static void LoadOverrides(GizmoSettingsRegistry registry, string filePath)
        {
            if (!File.Exists(filePath)) return;

            string json = File.ReadAllText(filePath);
            var records = JsonSerializer.Deserialize<List<SettingRecord>>(json);
            if (records == null) return;

            foreach (var rec in records)
            {
                uint hash = GizmoSettingsRegistry.ComputeHash(rec.key);

                // Forward-compat: register with a default placeholder if not yet known.
                if (!registry.IsRegistered(hash))
                    registry.RegisterSetting(rec.key, default);

                GizmoSettingValue value = ParseValue(rec.type, rec.value);
                registry.Write(hash, value);
            }
        }

        // ------------------------------------------------------------------ helpers

        private static string FormatValue(GizmoSettingValue v) => v.Type switch
        {
            SettingType.Bool    => v.BoolValue.ToString(),
            SettingType.Int32   => v.IntValue.ToString(CultureInfo.InvariantCulture),
            SettingType.Float32 => v.FloatValue.ToString(CultureInfo.InvariantCulture),
            _                   => string.Empty,
        };

        private static GizmoSettingValue ParseValue(string type, string value) => type switch
        {
            "Bool"    => GizmoSettingValue.From(bool.Parse(value)),
            "Int32"   => GizmoSettingValue.From(int.Parse(value, CultureInfo.InvariantCulture)),
            "Float32" => GizmoSettingValue.From(float.Parse(value, CultureInfo.InvariantCulture)),
            _         => default,
        };

        // ------------------------------------------------------------------ JSON model

        private sealed class SettingRecord
        {
            public string key   { get; set; } = string.Empty;
            public string type  { get; set; } = string.Empty;
            public string value { get; set; } = string.Empty;
        }
    }
}
