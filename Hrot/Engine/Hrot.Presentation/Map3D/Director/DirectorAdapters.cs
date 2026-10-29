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
        public void Caption(string? text) => Subtitle(text == null ? null : new SubtitleFrame(text, SubtitlePosition.Bottom, 1f));

        /// <summary>⭐ <c>CE-1303</c> P2b — the subtitle this overlay draws now (the model a rail reads).</summary>
        public SubtitleFrame? Shown { get; private set; }

        /// <summary>⭐ <c>CE-1303</c> P2b (§11o S9) — drawn on AND off the cinema: an iteration pass keeps the panels on screen and must still show the text.</summary>
        public void Subtitle(SubtitleFrame? frame)
        {
            Shown = frame;
            _caption = frame?.Text;
            if (frame != null && !_registered && _wm() is { } wm) { wm.RegisterFrameOverlay(Draw); _registered = true; }
        }

        private void Draw()
        {
            if (!Active)
            {
                if (Shown is { } sub && !string.IsNullOrWhiteSpace(sub.Text)) DrawSubtitle(ImGui.GetForegroundDrawList(), ImGui.GetIO().DisplaySize, ImGui.GetFont(), sub);
                return;
            }
            // ⭐⭐ CE-4081 — the cinema OWNS the keyboard: its one control is Esc, read from ImGui below. The cinema has hidden every panel, so ImGui would otherwise
            //   want no keyboard, and the map's canvas then DRAINS Raylib's one key queue for its layers BEFORE ImGui begins (CE-3175: one reader a frame) — the Esc
            //   never reached ImGui and the film could not be stopped. 📐 Measured on the real rlImGui 3.2.0 (Xvfb): a drained queue ⇒ ImGui sees 0 of 10 taps, with
            //   this request 10 of 10. A request for the NEXT frame (ImGui's own seam — what a text field does while it is typed in), so it is asked every frame.
            ImGui.SetNextFrameWantCaptureKeyboard(true);
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
            else if (Shown is { } sub && !string.IsNullOrWhiteSpace(_caption))
                DrawSubtitle(dl, size, font, sub);
            if (FilmStudio.Fade is > 0f and var fade && _card == null)   // ⭐ T4 / TE-8 — a dip's black over the picture
                dl.AddRectFilled(Vector2.Zero, size, (uint)Math.Round(fade * 255f) << 24);
            dl.AddText(new Vector2(8, size.Y - 20), 0x80FFFFFF, "Esc: stop the director");
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) EscapePressed?.Invoke();
        }

        /// <summary>The subtitle in a dark band — at the bottom (the caption's place), the top or the middle — its band and text at the subtitle's opacity.</summary>
        private static void DrawSubtitle(ImDrawListPtr dl, Vector2 size, ImFontPtr font, SubtitleFrame sub)
        {
            float s = SubtitleBand.TextSizeFor(size.Y);   // ⭐ CE-1370 — the band's geometry and opacity are SubtitleBand's, the same as in a captured frame
            var extent = font.CalcTextSizeA(s, size.X * 0.86f, size.X * 0.86f, sub.Text);
            var band = SubtitleBand.Of(sub, size.Y, extent.Y);
            dl.AddRectFilled(new Vector2(0, band.Top), new Vector2(size.X, band.Top + band.Height), (uint)band.BandAlpha << 24);
            dl.AddText(font, s, new Vector2((size.X - extent.X) / 2f, band.Top + s / 2f), ((uint)band.TextAlpha << 24) | 0x00FFFFFF, sub.Text, size.X * 0.86f);
        }
    }

    /// <summary>⭐ <c>CE-4038</c> B10 — the take's layers on the map's own layer control (a transient override, never saved).</summary>
    public sealed class MapLayerOverride : ILayerOverride
    {
        private readonly Func<Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo?> _layers;

        public MapLayerOverride(Func<Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo?> layers) => _layers = layers;

        /// <summary>Names the last <see cref="Apply"/> did not know.</summary>
        public IReadOnlyList<string> Unknown { get; private set; } = Array.Empty<string>();

        /// <summary>⭐ <c>CE-1304</c> P6 — the set last applied (null: cleared, or never) — what the take asked of the map, even on a host with no layer control
        /// (a headless browser): the model a rail reads, as <see cref="WindowCinema.Shown"/> is for the subtitle.</summary>
        public LayerChange? Applied { get; private set; }

        public void Apply(IReadOnlyList<string> on, IReadOnlyList<string> off)
        {
            Applied = new LayerChange(on, off);
            Unknown = _layers()?.SetOverride(on, off) ?? Array.Empty<string>();
        }

        public void Clear()
        {
            Applied = null;
            _layers()?.ClearOverride();
        }
    }

    /// <summary>⭐ <c>CE-4038</c> B8 — frames through the film's mailbox: the map on screen writes the scene (or a card) in its own update.</summary>
    public sealed class FilmFrameSink : IFrameSink
    {
        public Task Frame(string path, int width, int height, int supersample, SubtitleFrame? subtitle)
            => FilmStudio.Capture(new FilmCaptureRequest(path, width, height, supersample, subtitle));

        /// <summary>⭐ CE-1305 — the subtitle and the film graphics, burned in by the map that films (<c>FilmText.Graphics</c>).</summary>
        public Task Frame(string path, int width, int height, int supersample, FilmOverlay overlay)
            => FilmStudio.Capture(new FilmCaptureRequest(path, width, height, supersample, overlay.Subtitle, Graphics: overlay.Graphics));

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
            AudioDirector.Rate = null;
        }

        /// <summary>⭐ <c>CE-1305</c> TE-11 — the live mixer hears the scene at the film's rate (<see cref="AudioDirector.Rate"/>).</summary>
        public void SetRate(double rate) => AudioDirector.Rate = rate;

        public bool EndLog(string wavPath, IReadOnlyList<double> frameSimTimes, int fps)
        {
            var track = AudioDirector.Recorder;
            AudioDirector.Recorder = null;
            AudioDirector.TakeSound = null;
            AudioDirector.Rate = null;
            return track != null && track.RenderWav(wavPath, frameSimTimes, fps);
        }
    }
}
