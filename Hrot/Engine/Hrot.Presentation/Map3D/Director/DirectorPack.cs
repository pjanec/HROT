using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Presentation.WindowManager;

namespace Hrot.UI.Common.Map3D.Director
{
    /// <summary>
    /// ⭐ <c>CE-1303</c> P1 (docs/DESIGN_Director_Timeline_Editor.md §11j) — what a host hands <see cref="DirectorPack.Attach"/>: ITS film host,
    /// the window manager and layer control its adapters drive, its pilot camera and timeline, and — optionally — the one runner extension
    /// (the editor's verifier) and the Director window tabs it contributes. ⛔ Nothing here names a host.
    /// </summary>
    /// <param name="Host">The host the runner drives (load, preview, step, clock, the takes).</param>
    /// <param name="Wm">The window manager (late-bound: the cinema hides its panels).</param>
    /// <param name="Layers">The map's layer control (a take's layer override).</param>
    /// <param name="Headless">No renderer: no frame sink.</param>
    /// <param name="Pilot">The pilot camera as a key's camera (<see cref="PilotCamera.FromMap"/>).</param>
    /// <param name="Timeline">The host's Event Timeline panel; the pack gives it the open take's tracks and undo / redo.</param>
    /// <param name="Extension">The runner's one seam for a kind of run (null: films only).</param>
    /// <param name="WindowTabs">Tabs the Director window draws after its film tabs.</param>
    /// <param name="EventAt">CR-6: the nearest event of THIS host's timeline index (K hangs the key on it); null = no index.</param>
    /// <param name="TitleBarColor">The host's title-bar colour for the window.</param>
    public sealed record DirectorPackHost(
        IDirectorHost Host,
        Func<WindowManager?> Wm,
        Func<Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo?> Layers,
        bool Headless,
        Func<CameraSpec?> Pilot,
        TimelinePanel Timeline,
        IRunExtension? Extension = null,
        IReadOnlyList<IDirectorWindowTab>? WindowTabs = null,
        Func<double, EventAnchor?>? EventAt = null,
        Vector4? TitleBarColor = null);

    /// <summary>
    /// ⭐ <c>CE-1303</c> P1 (§11j, CR-2) — the ONE registrar of the film director: builds the runner (with the window / layer / audio / frame
    /// adapters), the take session and the window, and wires the host's Event Timeline to the open take. A host calls <see cref="Attach"/>
    /// once, <see cref="Tick"/> once per frame, and <see cref="RegisterWindow"/> where it registers its windows.
    /// </summary>
    public sealed class DirectorPack
    {
        private DirectorPack(DirectorRunner runner, DirectorTakeSession session, DirectorWindow window, MapLayerOverride layers, WindowCinema cinema, TakeStore takes)
        {
            Takes = takes; Runner = runner; Session = session; Window = window; Layers = layers; Cinema = cinema;
        }

        public DirectorRunner Runner { get; }
        /// <summary>The take open for editing (one <see cref="TakeEditor"/> shared by the window and the Event Timeline).</summary>
        public DirectorTakeSession Session { get; }
        public DirectorWindow Window { get; }
        public MapLayerOverride Layers { get; }
        public WindowCinema Cinema { get; }
        /// <summary>The scenarios root's takes and reels.</summary>
        public TakeStore Takes { get; }

        public static DirectorPack Attach(DirectorPackHost h)
        {
            if (h == null) throw new ArgumentNullException(nameof(h));
            var cinema = new WindowCinema(h.Wm);
            var layers = new MapLayerOverride(h.Layers);
            DirectorRunner? runner = null;
            runner = new DirectorRunner(h.Host, cinema, layers,
                h.Headless ? null : new FilmFrameSink(),
                new AudioDirectorSound(() => runner?.FilmSimTime ?? double.NaN),
                extension: h.Extension);
            cinema.EscapePressed = () => runner.Stop();
            // ⭐ CE-1364 T3 — the open take: the Event Timeline shows ITS Camera / Time tracks, Ctrl+Z on the panel undoes an edit of them
            var session = new DirectorTakeSession(() => h.Host.Takes, () => runner, h.Pilot, h.EventAt);
            h.Timeline.Tracks = () => session.Tracks;
            h.Timeline.Undo = () => session.UndoLast();
            h.Timeline.Redo = () => session.RedoLast();
            var window = new DirectorWindow(() => runner, () => h.Host.Takes, session, h.WindowTabs, h.TitleBarColor);
            return new DirectorPack(runner, session, window, layers, cinema, h.Host.Takes);
        }

        /// <summary>The director's frame: once per host frame, after the routes drained and before the map's update (§0, R-3038).</summary>
        public void Tick() => Runner.Tick();

        /// <summary>Puts the Director window (View › Director) among the host's windows.</summary>
        public void RegisterWindow(WindowManager wm) => wm.RegisterWindow(Window);
    }
}
