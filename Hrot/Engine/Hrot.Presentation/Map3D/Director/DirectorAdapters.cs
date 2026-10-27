using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Hrot.UI.Common.Audio;
using ImGuiNET;

namespace Hrot.UI.Common.Map3D.Director
{
    /// <summary>
    /// ⭐ <c>CE-4038</c> B9 (D-6, D-7) — the editor's cinema: the window manager's <see cref="Fdp.Presentation.WindowManager.WindowManager.Cinema"/>
    /// hides the menu bar and every panel (the map fills the window), and a frame overlay draws the card and the caption. ⭐ Esc stops the
    /// director (the only control left on screen).
    /// </summary>
    public sealed class WindowCinema : ICinema
    {
        private readonly Func<Fdp.Presentation.WindowManager.WindowManager?> _wm;
        private string? _card, _caption;
        private bool _registered;

        public WindowCinema(Func<Fdp.Presentation.WindowManager.WindowManager?> wm) => _wm = wm;

        /// <summary>Esc was pressed while the cinema showed (the runner's owner stops it).</summary>
        public Action? EscapePressed { get; set; }

        public bool Active { get; private set; }

        public void Enter()
        {
            Active = true;
            if (_wm() is { } wm)
            {
                wm.Cinema = true;
                if (!_registered) { wm.RegisterFrameOverlay(Draw); _registered = true; }
            }
        }

        public void Exit()
        {
            Active = false;
            if (_wm() is { } wm) wm.Cinema = false;
        }

        public void Card(string? text) => _card = text;
        public void Caption(string? text) => _caption = text;

        private void Draw()
        {
            if (!Active) return;
            var io = ImGui.GetIO();
            var dl = ImGui.GetForegroundDrawList();
            var size = io.DisplaySize;
            var font = ImGui.GetFont();
            if (_card != null)
            {
                dl.AddRectFilled(Vector2.Zero, size, 0xFF000000);
                var lines = _card.Split('\n');
                float big = MathF.Max(18f, size.Y / 14f), small = MathF.Max(14f, size.Y / 24f);
                float y = size.Y * 0.4f;
                for (int i = 0; i < lines.Length; i++)
                {
                    float s = i == lines.Length - 1 ? big : small;
                    var w = font.CalcTextSizeA(s, float.MaxValue, 0f, lines[i]).X;
                    dl.AddText(font, s, new Vector2((size.X - w) / 2f, y), i == lines.Length - 1 ? 0xFFFFFFFF : 0xFFC8C8C8, lines[i]);
                    y += s * 1.4f;
                }
            }
            else if (!string.IsNullOrWhiteSpace(_caption))
            {
                float s = MathF.Max(14f, size.Y / 30f);
                var w = font.CalcTextSizeA(s, size.X * 0.86f, size.X * 0.86f, _caption).X;
                var h = font.CalcTextSizeA(s, size.X * 0.86f, size.X * 0.86f, _caption).Y;
                float top = size.Y - h - s - size.Y / 18f;
                dl.AddRectFilled(new Vector2(0, top), new Vector2(size.X, top + h + s), 0x96000000);
                dl.AddText(font, s, new Vector2((size.X - w) / 2f, top + s / 2f), 0xFFFFFFFF, _caption, size.X * 0.86f);
            }
            if (FilmStudio.Fade is > 0f and var fade && _card == null)   // ⭐ T4 / TE-8 — a dip's black over the picture
                dl.AddRectFilled(Vector2.Zero, size, (uint)Math.Round(fade * 255f) << 24);
            dl.AddText(new Vector2(8, size.Y - 20), 0x80FFFFFF, "Esc: stop the director");
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) EscapePressed?.Invoke();
        }
    }

    /// <summary>⭐ <c>CE-4038</c> B10 — the take's layers on the map's own layer control (a transient override, never saved).</summary>
    public sealed class MapLayerOverride : ILayerOverride
    {
        private readonly Func<Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo?> _layers;

        public MapLayerOverride(Func<Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo?> layers) => _layers = layers;

        /// <summary>Names the last <see cref="Apply"/> did not know.</summary>
        public IReadOnlyList<string> Unknown { get; private set; } = Array.Empty<string>();

        public void Apply(IReadOnlyList<string> on, IReadOnlyList<string> off) => Unknown = _layers()?.SetOverride(on, off) ?? Array.Empty<string>();
        public void Clear() => _layers()?.ClearOverride();
    }

    /// <summary>⭐ <c>CE-4038</c> B8 — frames through the film's mailbox: the map on screen writes the scene (or a card) in its own update.</summary>
    public sealed class FilmFrameSink : IFrameSink
    {
        public Task Frame(string path, int width, int height, int supersample, string? caption)
            => FilmStudio.Capture(new FilmCaptureRequest(path, width, height, supersample, caption));

        public Task Card(string path, int width, int height, string text)
            => FilmStudio.Capture(new FilmCaptureRequest(path, width, height, 1, Card: text));
    }

    /// <summary>⭐ <c>CE-4038</c> B11 — the take's sound switch and the film's sound track, through <see cref="AudioDirector"/>.</summary>
    public sealed class AudioDirectorSound : IDirectorSound
    {
        private readonly Func<double> _filmSimTime;

        /// <param name="filmSimTime">The film's sim time now (the item's sim time plus its offset on the one track).</param>
        public AudioDirectorSound(Func<double> filmSimTime) => _filmSimTime = filmSimTime;

        public void SetTakeSound(bool? on) => AudioDirector.TakeSound = on ?? (AudioDirector.Recorder != null ? true : null);

        public void BeginLog()
        {
            AudioDirector.Recorder = new SoundTrack(_filmSimTime);
            AudioDirector.TakeSound = true;
        }

        public void Reset()
        {
            AudioDirector.Recorder = null;
            AudioDirector.TakeSound = null;
        }

        public bool EndLog(string wavPath, IReadOnlyList<double> frameSimTimes, int fps)
        {
            var track = AudioDirector.Recorder;
            AudioDirector.Recorder = null;
            AudioDirector.TakeSound = null;
            return track != null && track.RenderWav(wavPath, frameSimTimes, fps);
        }
    }
}
