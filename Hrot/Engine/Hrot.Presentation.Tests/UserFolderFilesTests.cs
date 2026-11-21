using System;
using System.IO;
using System.Text.Json;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Hrot.ScenarioEditor.Map;
using Hrot.UI.Common;
using Hrot.UI.Common.Audio;
using Hrot.UI.Common.Picker;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐ <c>CE-1441</c> (audit #16) — the per-user preference files are written by ONE helper (<see cref="NodeEditor.Core.IO.SafeTextFile"/>) at ONE
/// place in the user folder (<see cref="UserFiles"/>): <c>AudioSettings</c>, <c>MapLayerPreferences</c>, <c>RecentStore</c> (via <c>UserRecentPicks</c>)
/// and <c>UnitDebugSettingsMemory</c> (its rail is in <c>UnitDebugSettingsTests</c>). No suite owned the user-folder files as a group, so this is the
/// one that does; the audio / map-layer / recent feature rails stay where they were.
/// ⭐ S5 — a file the OLD code wrote still loads, and the new writers' files are readable by the old readers (same layout).
/// </summary>
public sealed class UserFolderFilesTests
{
    private static string NewDir() => Path.Combine(Path.GetTempPath(), "hrot-userfiles-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// ⭐ Every file is resolved by <see cref="UserFiles"/> — the same folder, whichever class asks.
    /// 🔴 Red-proof: have <c>MapLayerPreferences.DefaultPath</c> return <c>Path.Combine(Path.GetTempPath(), FileName)</c> ⇒ the map-layers row fails.
    /// </summary>
    [Fact]
    public void CE1441_EveryUserFile_IsResolvedByTheOneHelper_InTheOneFolder()
    {
        var folder = Path.GetDirectoryName(UserFiles.PathOf("x.json"));

        Assert.Equal(folder, Path.GetDirectoryName(AudioSettings.DefaultPath()));
        Assert.Equal(folder, Path.GetDirectoryName(MapLayerPreferences.DefaultPath()));
        Assert.Equal(folder, Path.GetDirectoryName(UserRecentPicks.DefaultPath()));
        Assert.Equal(folder, Path.GetDirectoryName(UnitDebugSettingsMemory.DefaultDirectory()));
        Assert.Equal(UserFiles.AppFolderName, AudioSettings.AppFolderName);
    }

    /// <summary>
    /// ⭐ The helper's write creates the folder and goes through a temp file: a save into a folder that does not exist yet SUCCEEDS and leaves no
    /// <c>.tmp</c>. (The old direct <c>File.WriteAllText</c> / <c>SaveToDisk</c> returned false / threw there.)
    /// 🔴 Red-proof: <c>AudioSettings.Save</c> back to <c>File.WriteAllText(path, …)</c> ⇒ the audio row fails; <c>MapLayerPreferences.Save</c> back to
    /// <c>settings.SaveToDisk(path)</c> ⇒ the map row fails; <c>RecentStore.Save</c> back to <c>File.WriteAllText</c> ⇒ the recent row fails.
    /// </summary>
    [Fact]
    public void CE1441_TheWriters_CreateTheFolder_AndLeaveNoTempFile()
    {
        var dir = NewDir();
        try
        {
            var audio = Path.Combine(dir, "a", "audio.json");
            Assert.True(new AudioSettings { Master = 0.3f }.Save(audio));
            Assert.Equal(0.3f, AudioSettings.Load(audio).Master, 3);

            var map = Path.Combine(dir, "m", "map-layers.json");
            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting("map.layers.test", GizmoSettingValue.From(false));
            reg.Write(GizmoSettingsRegistry.ComputeHash("map.layers.test"), GizmoSettingValue.From(true), scope: SettingScope.Global);
            Assert.True(MapLayerPreferences.Save(reg, map));

            var recent = Path.Combine(dir, "r", "recent-picks.json");
            var store = RecentStore.Load(recent);
            store.Push("recent.Scenario", "s1");
            Assert.True(File.Exists(recent));

            Assert.Empty(Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    /// <summary>
    /// ⭐ S5 — saved files keep their layout: what the OLD writers produced (the very expressions the old code used) loads through the new readers, and
    /// what the new writers produce loads through the OLD readers (<c>GizmoSettingsRegistry.LoadFromDisk</c>).
    /// 🔴 Red-proof: make <c>GizmoSettingsRegistry.ToJson</c> return <c>"[]"</c> ⇒ the map-layers assertions (new reader and old reader) fail.
    /// </summary>
    [Fact]
    public void CE1441_FilesTheOldCodeWrote_StillLoad_AndTheNewOnesLoadInTheOldReaders()
    {
        var dir = NewDir();
        Directory.CreateDirectory(dir);
        try
        {
            // audio — the old Save: Serialize(this, Indented) → File.WriteAllText
            var audio = Path.Combine(dir, "audio.json");
            var old = new AudioSettings { Master = 0.4f, Muted = true, CameraSound = true };
            File.WriteAllText(audio, JsonSerializer.Serialize(old, Fdp.Core.Serialization.FdpJsonOptionsRegistry.Indented));
            var read = AudioSettings.Load(audio);
            Assert.Equal((0.4f, true, true), (read.Master, read.Muted, read.CameraSound));

            // map layers — the old writer was the registry's own SaveToDisk (still there); the new reader takes it
            var map = Path.Combine(dir, "map-layers.json");
            var writer = new GizmoSettingsRegistry();
            writer.RegisterSetting("map.layers.test", GizmoSettingValue.From(false));
            var hash = GizmoSettingsRegistry.ComputeHash("map.layers.test");
            writer.Write(hash, GizmoSettingValue.From(true), scope: SettingScope.Global);
            writer.SaveToDisk(map, SettingScope.Global);

            var viaNew = new GizmoSettingsRegistry();
            viaNew.RegisterSetting("map.layers.test", GizmoSettingValue.From(false));
            MapLayerPreferences.Load(viaNew, map);
            Assert.True(viaNew.Read(hash).BoolValue);

            // … and the new writer's file loads in the old reader
            var map2 = Path.Combine(dir, "map2.json");
            Assert.True(MapLayerPreferences.Save(writer, map2));
            var viaOld = new GizmoSettingsRegistry();
            viaOld.RegisterSetting("map.layers.test", GizmoSettingValue.From(false));
            viaOld.LoadFromDisk(map2, SettingScope.Global);
            Assert.True(viaOld.Read(hash).BoolValue);

            // recent — the CE-4098 layout, written by hand as the old Save wrote it
            var recent = Path.Combine(dir, "recent-picks.json");
            File.WriteAllText(recent, "{\n  \"Version\": 1,\n  \"Lists\": { \"recent.Scenario\": [ \"new\", \"old\" ] }\n}");
            var store = RecentStore.Load(recent);
            Assert.Equal(new[] { "new", "old" }, store.GetRecent("recent.Scenario"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
