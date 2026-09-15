#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using NLog;

namespace HrotStrideApp;

/// <summary>
/// Persistent 3D camera bookmarks (BATCH-S2-AC). Stores up to 10 slots (0..9) of camera
/// pose (position + rotation) in %LOCALAPPDATA%\HROT\camera_bookmarks.json. Saved immediately
/// on each Save call. Slot 0 is the on-load default (restored at startup).
/// </summary>
public sealed class CameraBookmarkStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public sealed class Bookmark
    {
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
    }

    // System.Numerics.Vector3/Quaternion expose X/Y/Z/W as FIELDS; System.Text.Json ignores public
    // fields unless IncludeFields is set — without this the poses serialize as empty {} (BATCH-S2-AC).
    private static readonly JsonSerializerOptions JsonOpts = new() { IncludeFields = true, WriteIndented = true };

    private readonly string _path;
    private Dictionary<int, Bookmark> _slots = new();

    public CameraBookmarkStore()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HROT");
        try { Directory.CreateDirectory(dir); } catch { /* best effort */ }
        _path = Path.Combine(dir, "camera_bookmarks.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var data = JsonSerializer.Deserialize<Dictionary<int, Bookmark>>(json, JsonOpts);
            if (data != null) _slots = data;
            Log.Info("[CameraBookmarkStore] Loaded {0} bookmark(s) from {1}.", _slots.Count, _path);
        }
        catch (Exception ex) { Log.Warn("[CameraBookmarkStore] Load failed: {0}", ex.Message); }
    }

    private void Persist()
    {
        try
        {
            var json = JsonSerializer.Serialize(_slots, JsonOpts);
            File.WriteAllText(_path, json);
        }
        catch (Exception ex) { Log.Warn("[CameraBookmarkStore] Save failed: {0}", ex.Message); }
    }

    /// <summary>Save (and immediately persist) the pose into slot 0..9.</summary>
    public void Save(int slot, Vector3 position, Quaternion rotation)
    {
        _slots[slot] = new Bookmark { Position = position, Rotation = rotation };
        Persist();
        Log.Info("[CameraBookmarkStore] Saved camera bookmark slot {0} pos=({1:F2},{2:F2},{3:F2}).",
            slot, position.X, position.Y, position.Z);
    }

    /// <summary>Try to read slot 0..9.</summary>
    public bool TryGet(int slot, out Vector3 position, out Quaternion rotation)
    {
        if (_slots.TryGetValue(slot, out var b))
        {
            position = b.Position; rotation = b.Rotation; return true;
        }
        position = default; rotation = default; return false;
    }
}
