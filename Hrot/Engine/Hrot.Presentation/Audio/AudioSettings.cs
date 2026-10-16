using System;
using System.IO;
using System.Text.Json;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.UI.Common.Audio
{
    /// <summary>
    /// ⭐ CE-1120 (docs/designs/audio/DESIGN_FPS_Audio.md AU-9) — the OPERATOR's sound settings: master volume, mute and one volume per
    /// <see cref="SoundCategory"/>. Kept per user, never in the scenario (two operators want different volumes), in the same per-user
    /// folder as the layout (<see cref="LayoutPaths.UserDirectory"/>) but in its OWN file, so the development-time layout reset never
    /// touches it. A missing or unreadable file gives the defaults; a failed save is ignored — sound settings must never stop a host.
    /// </summary>
    public sealed class AudioSettings
    {
        public const string FileName = "audio.json";

        /// <summary>The per-user folder's name for this repo's apps (as <c>RaylibPresentationShell.AppFolderName</c>).</summary>
        public const string AppFolderName = "HROT";

        public float Master { get; set; } = 0.8f;
        public bool Muted { get; set; }

        /// <summary>⭐ CE-1137 — hear the world in the 3-D view outside FPS too, from the camera (Settings ▸ Sound in the 3-D view). Off by
        /// default: AU-3 keeps the scene silent unless a unit is possessed.</summary>
        public bool CameraSound { get; set; }

        /// <summary>One volume per <see cref="SoundCategory"/>, indexed by it.</summary>
        public float[] Categories { get; set; } = Defaults();

        public static int CategoryCount => Enum.GetValues<SoundCategory>().Length;

        private static float[] Defaults()
        {
            var a = new float[CategoryCount];
            Array.Fill(a, 1f);
            return a;
        }

        /// <summary>Where this user's file is (the folder is created).</summary>
        public static string DefaultPath() => Path.Combine(LayoutPaths.UserDirectory(AppFolderName), FileName);

        /// <summary>The settings in <paramref name="path"/>, else the defaults; null path ⇒ the defaults.</summary>
        public static AudioSettings Load(string? path)
        {
            var s = new AudioSettings();
            try
            {
                if (path != null && File.Exists(path) && JsonSerializer.Deserialize<AudioSettings>(File.ReadAllText(path), Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed) is { } read) s = read;
            }
            catch (Exception) { s = new AudioSettings(); }
            return s.Normalised();
        }

        /// <summary>Writes the settings; false (and nothing thrown) when it cannot. A null path saves nothing.</summary>
        public bool Save(string? path)
        {
            if (path == null) return false;
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(this, Fdp.Core.Serialization.FdpJsonOptionsRegistry.Indented));
                return true;
            }
            catch (Exception) { return false; }
        }

        private AudioSettings Normalised()
        {
            Master = Clamp(Master);
            var c = Defaults();
            if (Categories != null) for (int i = 0; i < c.Length && i < Categories.Length; i++) c[i] = Clamp(Categories[i]);
            Categories = c;
            return this;
        }

        private static float Clamp(float v) => float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 1f;
    }

    /// <summary>
    /// ⭐ CE-1137 — ONE settings object per file for the whole process: every map host's <see cref="AudioLayer"/> and the Settings menu share
    /// it, so a save from one never overwrites another's change with a stale copy.
    /// </summary>
    public static class AudioPreferences
    {
        private static readonly System.Collections.Generic.Dictionary<string, AudioSettings> ByPath = new();
        private static readonly object Gate = new();

        /// <summary>The settings in <paramref name="path"/> (loaded once); a null path ⇒ a fresh default object (tests).</summary>
        public static AudioSettings For(string? path)
        {
            if (path == null) return AudioSettings.Load(null);
            lock (Gate)
            {
                if (!ByPath.TryGetValue(path, out var s)) ByPath[path] = s = AudioSettings.Load(path);
                return s;
            }
        }

        /// <summary>⭐ CE-1137 — the operator's camera-sound switch (the default file), saved on change.</summary>
        public static bool CameraSound
        {
            get => For(SafeDefaultPath()).CameraSound;
            set { var s = For(SafeDefaultPath()); s.CameraSound = value; s.Save(SafeDefaultPath()); }
        }

        private static string? SafeDefaultPath()
        {
            try { return AudioSettings.DefaultPath(); } catch (Exception) { return null; }
        }
    }
}
