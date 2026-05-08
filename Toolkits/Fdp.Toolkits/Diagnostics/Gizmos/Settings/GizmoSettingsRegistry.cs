using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Fdp.Interfaces;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Settings
{
    /// <summary>
    /// In-memory store for all registered gizmo settings.
    /// Not thread-safe; access only from the ECS execute thread.
    /// </summary>
    public sealed class GizmoSettingsRegistry
    {
        private readonly Dictionary<uint, GizmoSettingValue> _active   = new();
        private readonly Dictionary<uint, GizmoSettingValue> _defaults = new();
        private readonly Dictionary<uint, string>            _keyNames = new();
        // GZ049: scope metadata — does not affect the hot read path.
        private readonly Dictionary<uint, SettingScope>      _scopes   = new();
        private bool _isDirty;

        public bool IsDirty => _isDirty;

        /// <summary>Optional cold-path notification; NOT called on the hot execute path.</summary>
        public event Action<uint>? OnSettingChanged;

        /// <summary>
        /// Registers a setting with its default value.
        /// If the key is already registered, the active value is preserved and only the default
        /// is updated when it differs (migration support).
        /// </summary>
        public void RegisterSetting(string keyName, GizmoSettingValue defaultValue)
        {
            uint hash = ComputeHash(keyName);
            _keyNames[hash] = keyName;
            if (!_active.ContainsKey(hash))
            {
                _active[hash]   = defaultValue;
                _defaults[hash] = defaultValue;
            }
            else if (_defaults.TryGetValue(hash, out var existing) && existing != defaultValue)
            {
                _defaults[hash] = defaultValue;
            }
        }

        /// <summary>Returns the current active value, or <c>default</c> if the hash is unknown.</summary>
        public GizmoSettingValue Read(uint keyHash)
            => _active.TryGetValue(keyHash, out var v) ? v : default;

        /// <summary>
        /// Writes a new active value and marks the registry dirty.
        /// If <paramref name="cmd"/> is non-null, publishes a <see cref="GizmoSettingChangedEvent"/>.
        /// </summary>
        public void Write(uint keyHash, GizmoSettingValue value, IEntityCommandBuffer? cmd = null, SettingScope scope = SettingScope.Global)
        {
            _active[keyHash] = value;
            _scopes[keyHash] = scope;
            _isDirty = true;
            cmd?.PublishEvent(new GizmoSettingChangedEvent { KeyHash = keyHash });
            OnSettingChanged?.Invoke(keyHash);
        }

        /// <summary>Returns the scope of the most recent write for this key, or Global when unknown.</summary>
        public SettingScope GetScope(uint keyHash)
            => _scopes.TryGetValue(keyHash, out var s) ? s : SettingScope.Global;

        /// <summary>Restores the active value to its registered default and clears the dirty flag.</summary>
        public void ResetToDefault(uint keyHash)
        {
            if (_defaults.TryGetValue(keyHash, out var def))
                _active[keyHash] = def;
            _isDirty = false;
        }

        /// <summary>FNV-1a 32-bit hash — identical algorithm used by <c>StringInternMap.Fnv1a32</c>.</summary>
        public static uint ComputeHash(string name)
        {
            uint h = 2166136261u;
            foreach (char c in name)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h;
        }

        /// <summary>Enumerates all registered settings (cold path — for UI and persistence).</summary>
        public IEnumerable<(string Key, GizmoSettingValue Active, GizmoSettingValue Default)> EnumerateAll()
        {
            foreach (var kv in _keyNames)
            {
                uint hash = kv.Key;
                _active.TryGetValue(hash, out var active);
                _defaults.TryGetValue(hash, out var def);
                yield return (kv.Value, active, def);
            }
        }

        /// <summary>Clears the dirty flag. Called by <see cref="GizmoSettingsPersistence"/> after a save.</summary>
        internal void ClearDirty() => _isDirty = false;

        /// <summary>Returns true when the given hash has a registered default.</summary>
        internal bool IsRegistered(uint hash) => _keyNames.ContainsKey(hash);

        // ── GZ049: scope-aware persistence ───────────────────────────────────

        /// <summary>
        /// Saves settings whose scope matches <paramref name="scope"/> to a JSON file.
        /// Settings with a different scope are excluded.
        /// </summary>
        public void SaveToDisk(string path, SettingScope scope = SettingScope.Global)
        {
            var records = new List<ScopeRecord>();
            foreach (var kv in _keyNames)
            {
                uint hash = kv.Key;
                if (GetScope(hash) != scope) continue;
                if (!_active.TryGetValue(hash, out var active)) continue;
                records.Add(new ScopeRecord
                {
                    key   = kv.Value,
                    type  = active.Type.ToString(),
                    value = FormatValue(active),
                });
            }
            File.WriteAllText(path, JsonSerializer.Serialize(records));
        }

        /// <summary>
        /// Loads settings from a JSON file and assigns them <paramref name="scope"/>.
        /// </summary>
        public void LoadFromDisk(string path, SettingScope scope = SettingScope.Global)
        {
            if (!File.Exists(path)) return;
            var records = JsonSerializer.Deserialize<List<ScopeRecord>>(File.ReadAllText(path));
            if (records == null) return;
            foreach (var rec in records)
            {
                uint hash = ComputeHash(rec.key);
                if (!IsRegistered(hash))
                    RegisterSetting(rec.key, default);
                Write(hash, ParseValue(rec.type, rec.value), cmd: null, scope: scope);
            }
        }

        /// <summary>
        /// Removes all in-memory overrides for the given scope and resets them to their defaults.
        /// Call at scenario unload (Session) or before loading a new project file (Project).
        /// </summary>
        public void DiscardScope(SettingScope scope)
        {
            foreach (var hash in _scopes.Keys.ToArray())
            {
                if (_scopes[hash] != scope) continue;
                ResetToDefault(hash);
                _scopes.Remove(hash);
            }
        }

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

        private sealed class ScopeRecord
        {
            public string key   { get; set; } = string.Empty;
            public string type  { get; set; } = string.Empty;
            public string value { get; set; } = string.Empty;
        }
    }
}
