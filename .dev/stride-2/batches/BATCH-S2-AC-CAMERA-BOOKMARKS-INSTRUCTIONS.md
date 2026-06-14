# BATCH-S2-AC — 3D camera bookmarks (save/recall slots, persisted)

## Goal
In the hosted Stride 3D view:
- **Ctrl+Alt+0..9** saves the current camera pose into slot 0..9.
- **Alt+0..9** (no Ctrl) recalls slot 0..9 (snaps the camera there).
- Bookmarks persist IMMEDIATELY to a JSON file in the user's local app data.
- On startup, if slot 0 exists, the camera is restored to it.

The camera is a free-fly `BasicCameraController` whose state lives entirely in
`_cameraEntity.Transform.Position` (Vector3) + `_cameraEntity.Transform.Rotation` (Quaternion) —
setting those directly is honored by the controller next frame (it accumulates onto the current
transform). So a bookmark = {Position, Rotation}. FOV is constant; do not persist it.

## Scope — TWO FILES

### File 1 (NEW): `Stride/HrotStrideApp.Game/CameraBookmarkStore.cs`
A small persistence helper. Mirror the existing app pattern in
`FDP/Engine/Fdp.Presentation/ImGui/Panels/WinFormsFileDialogService.cs` (System.Text.Json, folder
`%LOCALAPPDATA%\HROT`, WriteIndented, swallowed catches).

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using NLog;

namespace HrotStrideApp.Game; // MATCH the namespace used by StrideHrotGame.cs — verify and match it

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
            var data = JsonSerializer.Deserialize<Dictionary<int, Bookmark>>(json);
            if (data != null) _slots = data;
            Log.Info("[CameraBookmarkStore] Loaded {0} bookmark(s) from {1}.", _slots.Count, _path);
        }
        catch (Exception ex) { Log.Warn("[CameraBookmarkStore] Load failed: {0}", ex.Message); }
    }

    private void Persist()
    {
        try
        {
            var json = JsonSerializer.Serialize(_slots, new JsonSerializerOptions { WriteIndented = true });
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
```
VERIFY: System.Text.Json serializes `System.Numerics.Vector3`/`Quaternion` by their public X/Y/Z(/W)
fields — it does. If for any reason it doesn't round-trip in this project's STJ version, store
`float[]` arrays instead and convert. Confirm the namespace matches StrideHrotGame.cs.

### File 2: `Stride/HrotStrideApp.Game/StrideHrotGame.cs`
1. Add a field near `_cameraEntity` (~line 172):
```csharp
private readonly CameraBookmarkStore _cameraBookmarks = new(); // BATCH-S2-AC
```
2. In `Update(GameTime gameTime)`, right AFTER the existing `C`-key center block (~line 433, inside
   the same `_cameraEntity != null` guarded region), add bookmark handling:
```csharp
// BATCH-S2-AC: camera bookmarks. Ctrl+Alt+N saves slot N; Alt+N recalls slot N (N = 0..9).
if (_cameraEntity != null)
{
    bool ctrl = Input.IsKeyDown(Keys.LeftCtrl) || Input.IsKeyDown(Keys.RightCtrl);
    bool alt  = Input.IsKeyDown(Keys.LeftAlt)  || Input.IsKeyDown(Keys.RightAlt);
    if (alt)
    {
        // Digit keys D0..D9 map to slots 0..9.
        for (int slot = 0; slot <= 9; slot++)
        {
            var key = (Keys)((int)Keys.D0 + slot); // VERIFY Keys.D0..D9 are contiguous in this slot order
            if (!Input.IsKeyPressed(key)) continue;
            if (ctrl)
            {
                _cameraBookmarks.Save(slot, _cameraEntity.Transform.Position, _cameraEntity.Transform.Rotation);
            }
            else if (_cameraBookmarks.TryGet(slot, out var pos, out var rot))
            {
                _cameraEntity.Transform.Position = pos;
                _cameraEntity.Transform.Rotation = rot;
                Log.Info("[StrideHrotGame] Recalled camera bookmark slot {0}.", slot);
            }
        }
    }
}
```
   VERIFY in Stride.Input.Keys that `D0..D9` are contiguous and ordered 0..9. If NOT, replace the
   `(Keys)((int)Keys.D0 + slot)` with an explicit `Keys[]` lookup array `{ Keys.D0, Keys.D1, ..., Keys.D9 }`.
   `Input.IsKeyDown` / `Input.IsKeyPressed` are used elsewhere in this file (the C-key block) — match.

3. Index-0 restore on startup: in `BootEditorSubsystem()`, AFTER `AddFixedCamera(scene)` (the call
   that sets `_cameraEntity`, ~line 829), add:
```csharp
// BATCH-S2-AC: restore the default camera bookmark (slot 0) on load, if present.
if (_cameraEntity != null && _cameraBookmarks.TryGet(0, out var camPos0, out var camRot0))
{
    _cameraEntity.Transform.Position = camPos0;
    _cameraEntity.Transform.Rotation = camRot0;
    Log.Info("[StrideHrotGame] Restored default camera bookmark (slot 0) on load.");
}
```
   (Confirm `_cameraEntity` is assigned by the time this runs — the investigation says it is set at
   the end of AddFixedCamera; place this strictly after that call.)

## Constraints
- TWO files. Don't change BasicCameraController, the camera creation, or any other input handling.
- Recall must SNAP (set transform directly); the free-fly controller continues from there next frame.
- Saving must persist to disk immediately (Save → Persist).
- Don't intercept plain digit presses (no modifier) — only Alt (recall) / Ctrl+Alt (save).

## Acceptance
- Builds clean (`Stride/HrotStrideApp.sln`).
- (User) Frame a view, press Ctrl+Alt+1 → file `%LOCALAPPDATA%\HROT\camera_bookmarks.json` gains slot
  "1". Move the camera, press Alt+1 → camera snaps back to the saved view. Restart the app → if slot 0
  was saved, the camera starts there. Plain number keys still do nothing camera-related.
