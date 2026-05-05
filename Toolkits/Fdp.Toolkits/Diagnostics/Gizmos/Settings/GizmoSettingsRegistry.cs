using System;
using System.Collections.Generic;
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
        public void Write(uint keyHash, GizmoSettingValue value, IEntityCommandBuffer? cmd = null)
        {
            _active[keyHash] = value;
            _isDirty = true;
            cmd?.PublishEvent(new GizmoSettingChangedEvent { KeyHash = keyHash });
            OnSettingChanged?.Invoke(keyHash);
        }

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
    }
}
